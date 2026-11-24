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
using static Cantus.Client.Tests.LocalPlayback.LocalPlayers;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class LocalPlaybackBootstrapTests
{
    private const string LOCAL_SESSION_ID = "session-local-1";

    private readonly Mock<ISignalRPlaybackClient> _client = new();
    private readonly Mock<ILocalSessionApi> _sessionApi = new();
    private readonly FakeLocalPlayerSource _source = new();
    private readonly FakeLocalPlaybackSink _sink = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly LocalPlayerPreferences _preferences;
    private readonly LocalPlaybackPublisher _publisher;

    public LocalPlaybackBootstrapTests()
    {
        _preferences = new LocalPlayerPreferences(_settings);
        _publisher = new LocalPlaybackPublisher(
            _source,
            _sink,
            new PlayerSelectionPolicy(),
            _preferences,
            new FakeTimeProvider());
        _client.Setup(c => c.ReconnectWithTokenAsync(It.IsAny<string?>())).Returns(Task.CompletedTask);
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
    public async Task Start_WithoutASession_StartsTheSourceButCreatesNoSession()
    {
        // Arrange
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        int availabilityChanges = 0;
        bootstrap.AvailabilityChanged += () => availabilityChanges++;

        // Act
        await bootstrap.StartAsync();
        _client.Raise(c => c.Connected += null);

        // Assert
        _source.StartCount.Should().Be(1);
        bootstrap.IsSupported.Should().BeTrue();
        availabilityChanges.Should().Be(1);
        bootstrap.IsPublishing.Should().BeFalse();
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(c => c.ReconnectWithTokenAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesALocalSessionReconnectsAndPublishes()
    {
        // Arrange
        _sessionApi
            .Setup(a => a.CreateAsync("jaret-desktop", "JOINCODE1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Created());
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();

        // Act
        LocalSessionResult result = await bootstrap.CreateSessionAsync("JOINCODE1");

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Created);
        _client.Verify(c => c.ReconnectWithTokenAsync(LOCAL_SESSION_ID), Times.Once);
        bootstrap.IsPublishing.Should().BeTrue();
    }

    [Theory]
    [InlineData(LocalSessionStatus.Disabled)]
    [InlineData(LocalSessionStatus.JoinCodeRequired)]
    [InlineData(LocalSessionStatus.JoinCodeInvalid)]
    [InlineData(LocalSessionStatus.LimitReached)]
    [InlineData(LocalSessionStatus.RateLimited)]
    [InlineData(LocalSessionStatus.Failed)]
    public async Task CreateSessionAsync_WhenTheServerRefuses_ReturnsTheRefusalAndStaysSignedOut(LocalSessionStatus status)
    {
        // Arrange
        _sessionApi
            .Setup(a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalSessionResult(status));
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();

        // Act
        LocalSessionResult result = await bootstrap.CreateSessionAsync(null);
        _client.Raise(c => c.Connected += null);

        // Assert
        result.Status.Should().Be(status);
        _client.Verify(c => c.ReconnectWithTokenAsync(It.IsAny<string?>()), Times.Never);
        bootstrap.IsPublishing.Should().BeFalse();
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "a refusal is never retried on its own");
    }

    [Fact]
    public async Task CreateSessionAsync_WhenTheRequestThrows_ReportsAFailure()
    {
        // Arrange
        _sessionApi
            .Setup(a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();

        // Act
        LocalSessionResult result = await bootstrap.CreateSessionAsync(null);

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Failed);
        _client.Verify(c => c.ReconnectWithTokenAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task CreateSessionAsync_OnAPairedDisplay_RefusesWithoutAskingTheServer()
    {
        // Arrange
        _client.SetupGet(c => c.DisplayToken).Returns("display-token");
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();

        // Act
        LocalSessionResult result = await bootstrap.CreateSessionAsync(null);

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Failed);
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Start_OnAnUnsupportedPlatform_OffersNothing()
    {
        // Arrange
        _source.IsSupported = false;
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        int availabilityChanges = 0;
        bootstrap.AvailabilityChanged += () => availabilityChanges++;

        // Act
        await bootstrap.StartAsync();
        RaiseSessions(AuthorizedSessionPayload.KIND_LOCAL);
        LocalSessionResult result = await bootstrap.CreateSessionAsync(null);

        // Assert
        _source.StartCount.Should().Be(0);
        bootstrap.IsSupported.Should().BeFalse();
        availabilityChanges.Should().Be(0);
        bootstrap.IsPublishing.Should().BeFalse();
        result.Status.Should().Be(LocalSessionStatus.Failed);
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Start_WhenTheBusCannotBeReached_OffersNothing()
    {
        // Arrange
        _source.SupportedAfterStart = false;
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();

        // Act
        await bootstrap.StartAsync();
        RaiseSessions(AuthorizedSessionPayload.KIND_LOCAL);

        // Assert
        bootstrap.IsSupported.Should().BeFalse();
        bootstrap.IsPublishing.Should().BeFalse();
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
    public async Task NotifySignedOut_StopsPublishingAndNothingCreatesASessionAgain()
    {
        // Arrange
        _client.SetupGet(c => c.SessionToken).Returns(LOCAL_SESSION_ID);
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();
        RaiseSessions(AuthorizedSessionPayload.KIND_LOCAL);
        bootstrap.IsPublishing.Should().BeTrue();

        // Act
        bootstrap.NotifySignedOut();
        _client.SetupGet(c => c.SessionToken).Returns((string?)null);
        _client.Raise(c => c.Connected += null);

        // Assert
        bootstrap.IsPublishing.Should().BeFalse();
        _sessionApi.Verify(
            a => a.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    [Fact]
    public async Task SetPlayerEnabled_SavesTheSwitchAndFollowsAnotherPlayerRightAway()
    {
        // Arrange
        _client.SetupGet(c => c.SessionToken).Returns(LOCAL_SESSION_ID);
        _source.SetPlayers(
            Player("spotify", LocalPlayerStatus.Playing, Song(), startedAt: TimeSpan.FromSeconds(1)),
            Player("brave", LocalPlayerStatus.Playing, Song("Other"), startedAt: TimeSpan.FromSeconds(2)));
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        await bootstrap.StartAsync();
        RaiseSessions(AuthorizedSessionPayload.KIND_LOCAL);
        bootstrap.FollowedPlayer!.AppId.Should().Be("brave");
        int followedChanges = 0;
        bootstrap.FollowedPlayerChanged += () => followedChanges++;

        // Act
        bootstrap.SetPlayerEnabled("brave", false);

        // Assert
        _preferences.IsEnabled("brave").Should().BeFalse();
        bootstrap.IsPlayerEnabled("brave").Should().BeFalse();
        bootstrap.FollowedPlayer!.AppId.Should().Be("spotify");
        followedChanges.Should().Be(1);
    }

    [Fact]
    public async Task PlayersChanged_IsForwardedFromTheSourceOnceStarted()
    {
        // Arrange
        LocalPlaybackBootstrap bootstrap = CreateBootstrap();
        int changes = 0;
        bootstrap.PlayersChanged += () => changes++;
        _source.SetPlayers(Player("spotify", LocalPlayerStatus.Paused, Song()));
        changes.Should().Be(0, "nothing is forwarded before the source starts");

        // Act
        await bootstrap.StartAsync();
        _source.SetPlayers(Player("spotify", LocalPlayerStatus.Playing, Song()));

        // Assert
        changes.Should().Be(1);
        bootstrap.Players.Should().ContainSingle();
    }

    private LocalPlaybackBootstrap CreateBootstrap(string? joinCodePrefill = null) =>
        new(_client.Object, _source, _sessionApi.Object, _publisher, _preferences, "jaret-desktop", joinCodePrefill);

    private static LocalSessionResult Created() =>
        new(
            LocalSessionStatus.Created,
            new AuthorizedSessionPayload { Id = LOCAL_SESSION_ID, Kind = AuthorizedSessionPayload.KIND_LOCAL });

    private void RaiseSessions(string kind) =>
        _client.Raise(
            c => c.SessionsReceived += null,
            (IReadOnlyList<AuthorizedSessionPayload>)[new AuthorizedSessionPayload { Id = LOCAL_SESSION_ID, Kind = kind }]);
}
