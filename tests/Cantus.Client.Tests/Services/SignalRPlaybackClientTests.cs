using System;
using System.Collections.Generic;
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
            TimeSpan.FromMilliseconds(100));
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
}
