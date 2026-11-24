using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class SignalRPlaybackClientTests
{
    [Fact]
    public void Constructor_DefaultReconnectInterval_IsFiveSeconds()
    {
        // Arrange & Act
        SignalRPlaybackClient client = new();

        // Assert
        client.ReconnectInterval.Should().Be(TimeSpan.FromSeconds(5));
        client.State.Should().Be(HubConnectionState.Disconnected);
    }

    [Fact]
    public void Constructor_CustomReconnectInterval_SetsConfiguredInterval()
    {
        // Arrange & Act
        SignalRPlaybackClient client = new(
            "http://localhost:59999/hubs/playback",
            null,
            TimeSpan.FromSeconds(2));

        // Assert
        client.ReconnectInterval.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TryConnectAsync_WhenServerUnreachable_ReturnsFalseAndTransitionsToDisconnected()
    {
        // Arrange
        List<string> stateChanges = new();
        SignalRPlaybackClient client = new("http://127.0.0.1:59998/hubs/playback");
        client.ConnectionStateChanged += state => stateChanges.Add(state);

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(500));

        // Act
        bool result = await client.TryConnectAsync(cts.Token);

        // Assert
        result.Should().BeFalse();
        client.State.Should().Be(HubConnectionState.Disconnected);
        stateChanges.Should().Contain("Connecting");
        stateChanges.Should().Contain("Disconnected");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_WhenServerUnreachable_StartsPollerWithoutThrowing()
    {
        // Arrange
        List<string> stateChanges = new();
        SignalRPlaybackClient client = new(
            "http://127.0.0.1:59997/hubs/playback",
            null,
            TimeSpan.FromSeconds(5));
        client.ConnectionStateChanged += state => stateChanges.Add(state);

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(500));

        // Act
        Func<Task> act = async () => await client.StartAsync(cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
        client.State.Should().Be(HubConnectionState.Disconnected);
        stateChanges.Should().Contain("Connecting");
        stateChanges.Should().Contain("Disconnected");

        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DisposesPollerAndCanBeCalledMultipleTimes()
    {
        // Arrange
        SignalRPlaybackClient client = new("http://127.0.0.1:59996/hubs/playback");

        // Act
        Func<Task> act1 = async () => await client.DisposeAsync();
        Func<Task> act2 = async () => await client.DisposeAsync();

        // Assert
        await act1.Should().NotThrowAsync();
        await act2.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("session-token", null, "session-token")]
    [InlineData(null, "display-token", "display-token")]
    [InlineData("session-token", "display-token", "session-token")]
    [InlineData(null, null, null)]
    [InlineData(" ", " ", null)]
    [InlineData("", "display-token", "display-token")]
    public void SelectConnectionCredential_PrefersTheSessionThenTheDisplayToken(
        string? sessionToken,
        string? displayToken,
        string? expected)
    {
        // Act & Assert
        SignalRPlaybackClient.SelectConnectionCredential(sessionToken, displayToken).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("local-session", null, true)]
    [InlineData("session-token", "display-token", true)]
    [InlineData(null, "display-token", false)]
    [InlineData(" ", "display-token", false)]
    public void ShouldRegisterForSignIn_HoldsForEveryConnectionButADisplays(
        string? sessionToken,
        string? displayToken,
        bool expected)
    {
        // Act & Assert
        SignalRPlaybackClient.ShouldRegisterForSignIn(sessionToken, displayToken).Should().Be(expected);
    }

    [Fact]
    public async Task ReconnectWithDisplayToken_DropsAStaleSessionSoTheDisplayTokenIsSent()
    {
        // Arrange
        string sessionFile = Path.Combine(Path.GetTempPath(), $"cantus-session-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(sessionFile, "stale-session");
        SignalRPlaybackClient client = new(
            "http://127.0.0.1:59995/hubs/playback",
            "stale-session",
            TimeSpan.FromMinutes(5),
            sessionFile);

        try
        {
            // Act
            await client.ReconnectWithDisplayTokenAsync("display-token");

            // Assert
            client.SessionToken.Should().BeNull();
            client.DisplayToken.Should().Be("display-token");
            client.ConnectionCredential.Should().Be("display-token");
            File.Exists(sessionFile).Should().BeFalse();
        }
        finally
        {
            await client.DisposeAsync();
            File.Delete(sessionFile);
        }
    }

    [Fact]
    public async Task ReconnectWithoutDisplayToken_KeepsTheSession()
    {
        // Arrange
        string sessionFile = Path.Combine(Path.GetTempPath(), $"cantus-session-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(sessionFile, "session-token");
        SignalRPlaybackClient client = new(
            "http://127.0.0.1:59994/hubs/playback",
            "session-token",
            TimeSpan.FromMinutes(5),
            sessionFile);

        try
        {
            // Act
            await client.ReconnectWithDisplayTokenAsync(null);

            // Assert
            client.SessionToken.Should().Be("session-token");
            client.ConnectionCredential.Should().Be("session-token");
            File.Exists(sessionFile).Should().BeTrue();
        }
        finally
        {
            await client.DisposeAsync();
            File.Delete(sessionFile);
        }
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("ERROR", true)]
    [InlineData("3f2a9c0d1e4b4f6a8b7c9d0e1f2a3b4c", false)]
    public void IsReservedAuthQueryValue_RejectsSignInOutcomesAsSessionTokens(string value, bool expected)
    {
        SignalRPlaybackClient.IsReservedAuthQueryValue(value).Should().Be(expected);
    }
}
