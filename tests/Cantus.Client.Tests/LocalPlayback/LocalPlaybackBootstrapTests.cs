using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class LocalPlaybackBootstrapTests
{
    private const string LOCAL_SESSION_ID = "session-local-1";

    private readonly Mock<ISignalRPlaybackClient> _client = new();
    private readonly Mock<ILocalSessionApi> _sessionApi = new();
    private readonly FakeLocalPlayerSource _source = new();
    private readonly FakeLocalPlaybackSink _sink = new();
    private readonly LocalPlaybackPublisher _publisher;

    public LocalPlaybackBootstrapTests()
    {
        _publisher = new LocalPlaybackPublisher(
            _source,
            _sink,
            new PlayerSelectionPolicy(),
            new LocalPlayerPreferences(new InMemorySettingsStore()),
            new FakeTimeProvider());
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData(null, false)]
    public void IsEnabled_OnlyForOne(string? value, bool expected)
    {
        LocalPlaybackBootstrap.IsEnabled(name => name == LocalPlaybackBootstrap.ENABLE_VARIABLE ? value : null)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("jaret-desktop", "jaret-desktop")]
    [InlineData("  pc  ", "pc")]
    [InlineData("", LocalPlaybackBootstrap.FALLBACK_DISPLAY_NAME)]
    [InlineData(null, LocalPlaybackBootstrap.FALLBACK_DISPLAY_NAME)]
    public void NormalizeDisplayName_TrimsAndFallsBack(string? name, string expected)
    {
        LocalPlaybackBootstrap.NormalizeDisplayName(name).Should().Be(expected);
    }

    [Fact]
    public void NormalizeDisplayName_CapsTheLength()
    {
        LocalPlaybackBootstrap.NormalizeDisplayName(new string('x', 100))
            .Should().HaveLength(LocalPlaybackBootstrap.MAX_DISPLAY_NAME_LENGTH);
    }

    [Fact]
    public async Task Start_WithoutASession_CreatesALocalSessionAndReconnectsWithIt()
    {
        // Arrange
        _sessionApi
            .Setup(a => a.CreateAsync("jaret-desktop", "JOINCODE1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalSessionResult(
                LocalSessionStatus.Created,
                new AuthorizedSessionPayload { Id = LOCAL_SESSION_ID, Kind = AuthorizedSessionPayload.KIND_LOCAL }));
        LocalPlaybackBootstrap bootstrap = CreateBootstrap(joinCode: "JOINCODE1");

        // Act
        await bootstrap.StartAsync();

        // Assert
        _source.StartCount.Should().Be(1);
        _client.Verify(c => c.ReconnectWithTokenAsync(LOCAL_SESSION_ID), Times.Once);
    }

    [Theory]
    [InlineData(LocalSessionStatus.Disabled)]
    [InlineData(LocalSessionStatus.JoinCodeRequired)]
    [InlineData(LocalSessionStatus.JoinCodeInvalid)]
    [InlineData(LocalSessionStatus.LimitReached)]
    [InlineData(LocalSessionStatus.Failed)]
    public async Task Start_WhenTheServerRefuses_DoesNothingElse(LocalSessionStatus status)
    {
        // Arrange
        _sessionApi
            .Setup(a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalSessionResult(status));
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();

        // Act
        await bootstrap.StartAsync();

        // Assert
        _client.Verify(c => c.ReconnectWithTokenAsync(It.IsAny<string?>()), Times.Never);
        bootstrap.IsPublishing.Should().BeFalse();
    }

    [Fact]
    public async Task Start_WithASession_DoesNotCreateAnother()
    {
        // Arrange
        _client.SetupGet(c => c.SessionToken).Returns("existing");
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();

        // Act
        await bootstrap.StartAsync();

        // Assert
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Start_OnAPairedDisplay_DoesNotCreateALocalSession()
    {
        // Arrange
        _client.SetupGet(c => c.DisplayToken).Returns("display-token");
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();

        // Act
        await bootstrap.StartAsync();
        _client.Raise(c => c.Connected += null);

        // Assert
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(c => c.ReconnectWithTokenAsync(It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(LocalSessionStatus.Failed)]
    [InlineData(LocalSessionStatus.RateLimited)]
    public async Task Start_WhenCreationMayPass_RetriesOnTheNextConnect(LocalSessionStatus firstStatus)
    {
        // Arrange
        _sessionApi
            .SetupSequence(a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalSessionResult(firstStatus))
            .ReturnsAsync(new LocalSessionResult(
                LocalSessionStatus.Created,
                new AuthorizedSessionPayload { Id = LOCAL_SESSION_ID, Kind = AuthorizedSessionPayload.KIND_LOCAL }));
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();
        _client.Verify(c => c.ReconnectWithTokenAsync(It.IsAny<string?>()), Times.Never);

        // Act
        _client.Raise(c => c.Connected += null);
        _client.Raise(c => c.Connected += null);

        // Assert
        _client.Verify(c => c.ReconnectWithTokenAsync(LOCAL_SESSION_ID), Times.Once);
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData(LocalSessionStatus.Disabled)]
    [InlineData(LocalSessionStatus.JoinCodeRequired)]
    [InlineData(LocalSessionStatus.JoinCodeInvalid)]
    [InlineData(LocalSessionStatus.InvalidDisplayName)]
    [InlineData(LocalSessionStatus.LimitReached)]
    public async Task Start_WhenTheServerRefusesForGood_DoesNotRetryOnConnect(LocalSessionStatus status)
    {
        // Arrange
        _sessionApi
            .Setup(a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalSessionResult(status));
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();

        // Act
        _client.Raise(c => c.Connected += null);

        // Assert
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Start_OnAnUnsupportedPlatform_DoesNothing()
    {
        // Arrange
        _source.IsSupported = false;
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();

        // Act
        await bootstrap.StartAsync();
        RaiseSessions(AuthorizedSessionPayload.KIND_LOCAL);

        // Assert
        _source.StartCount.Should().Be(0);
        bootstrap.IsPublishing.Should().BeFalse();
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Start_WhenTheBusCannotBeReached_DoesNothing()
    {
        // Arrange
        _source.SupportedAfterStart = false;
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();

        // Act
        await bootstrap.StartAsync();
        RaiseSessions(AuthorizedSessionPayload.KIND_LOCAL);

        // Assert
        bootstrap.IsPublishing.Should().BeFalse();
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Publishing_RunsOnlyWhileTheSessionIsLocal()
    {
        // Arrange
        _client.SetupGet(c => c.SessionToken).Returns(LOCAL_SESSION_ID);
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        RaiseSessions(AuthorizedSessionPayload.KIND_LOCAL);
        bootstrap.IsPublishing.Should().BeFalse("the source has not started yet");

        // Act & Assert
        await bootstrap.StartAsync();
        bootstrap.IsPublishing.Should().BeTrue();

        RaiseSessions(AuthorizedSessionPayload.KIND_SPOTIFY);
        bootstrap.IsPublishing.Should().BeFalse();

        _client.Raise(c => c.AuthSessionReceived += null, new AuthorizedSessionPayload { Id = "x", Kind = "Local" });
        bootstrap.IsPublishing.Should().BeTrue();

        _client.Raise(c => c.SessionRevoked += null, LOCAL_SESSION_ID);
        bootstrap.IsPublishing.Should().BeFalse();
    }

    [Fact]
    public async Task SessionList_UsesTheEntryForTheHeldToken()
    {
        // Arrange
        _client.SetupGet(c => c.SessionToken).Returns(LOCAL_SESSION_ID);
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();

        // Act
        _client.Raise(
            c => c.SessionsReceived += null,
            (IReadOnlyList<AuthorizedSessionPayload>)
            [
                new AuthorizedSessionPayload { Id = "other", Kind = AuthorizedSessionPayload.KIND_SPOTIFY },
                new AuthorizedSessionPayload { Id = LOCAL_SESSION_ID, Kind = AuthorizedSessionPayload.KIND_LOCAL },
            ]);

        // Assert
        bootstrap.IsPublishing.Should().BeTrue();
    }

    private LocalPlaybackBootstrap CreateBootstrap(string? joinCode = null) =>
        new(_client.Object, _source, _sessionApi.Object, _publisher, "jaret-desktop", joinCode);

    private void RaiseSessions(string kind) =>
        _client.Raise(
            c => c.SessionsReceived += null,
            (IReadOnlyList<AuthorizedSessionPayload>)[new AuthorizedSessionPayload { Id = LOCAL_SESSION_ID, Kind = kind }]);
}
