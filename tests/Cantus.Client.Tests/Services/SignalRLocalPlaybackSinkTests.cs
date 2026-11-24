using System.Threading.Tasks;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class SignalRLocalPlaybackSinkTests
{
    private readonly Mock<ISignalRPlaybackClient> _client = new();

    [Fact]
    public void ServerClockOffset_IsNullUntilTheClockIsSynced()
    {
        // Arrange
        _client.SetupGet(c => c.ClockOffsetMs).Returns(-120);
        SignalRLocalPlaybackSink sink = new(_client.Object);

        // Act & Assert
        _client.SetupGet(c => c.IsClockSynced).Returns(false);
        sink.ServerClockOffsetMs.Should().BeNull();
        _client.SetupGet(c => c.IsClockSynced).Returns(true);
        sink.ServerClockOffsetMs.Should().Be(-120);
    }

    [Theory]
    [InlineData(HubConnectionState.Connected, true)]
    [InlineData(HubConnectionState.Reconnecting, false)]
    [InlineData(HubConnectionState.Disconnected, false)]
    public void IsConnected_FollowsTheHubState(HubConnectionState state, bool expected)
    {
        // Arrange
        _client.SetupGet(c => c.State).Returns(state);

        // Act & Assert
        new SignalRLocalPlaybackSink(_client.Object).IsConnected.Should().Be(expected);
    }

    [Fact]
    public async Task Publish_AndConnected_GoThroughTheClient()
    {
        // Arrange
        LocalPlaybackUpdatePayload payload = new() { Sequence = 3 };
        _client.Setup(c => c.PublishLocalPlaybackAsync(payload)).ReturnsAsync((int)LocalPublishResult.RateLimited);
        SignalRLocalPlaybackSink sink = new(_client.Object);
        int connected = 0;
        sink.Connected += () => connected++;

        // Act
        int result = await sink.PublishAsync(payload);
        _client.Raise(c => c.Connected += null);

        // Assert
        result.Should().Be((int)LocalPublishResult.RateLimited);
        connected.Should().Be(1);
    }

    [Fact]
    public async Task PublishLocalPlayback_WithoutAConnection_ReturnsNotConnected()
    {
        // Arrange
        await using SignalRPlaybackClient client = new("http://127.0.0.1:1/hubs/playback", sessionToken: "t", sessionFilePath: "/nonexistent/session.txt");

        // Act
        int result = await client.PublishLocalPlaybackAsync(new LocalPlaybackUpdatePayload());

        // Assert
        result.Should().Be((int)LocalPublishResult.NotConnected);
        client.IsClockSynced.Should().BeFalse();
    }
}
