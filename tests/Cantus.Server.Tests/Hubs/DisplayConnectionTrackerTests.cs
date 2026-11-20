using Cantus.Server.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Hubs;

public sealed class DisplayConnectionTrackerTests
{
    private const string DISPLAY_ID = "display-1";
    private const string OTHER_DISPLAY_ID = "display-2";
    private const int REVOKED_RETENTION_SECONDS = 60;

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly Mock<IHubContext<PlaybackHub, IPlaybackClient>> _mockHubContext = new();
    private readonly Mock<IHubClients<IPlaybackClient>> _mockClients = new();
    private readonly Mock<IPlaybackClient> _mockTargets = new();
    private readonly List<IReadOnlyList<string>> _notifiedConnectionIds = new();
    private readonly DisplayConnectionTracker _tracker;

    public DisplayConnectionTrackerTests()
    {
        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients
            .Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(ids => _notifiedConnectionIds.Add(ids))
            .Returns(_mockTargets.Object);

        _tracker = new DisplayConnectionTracker(
            _mockHubContext.Object,
            _timeProvider,
            NullLogger<DisplayConnectionTracker>.Instance);
    }

    [Fact]
    public void Add_TracksConnectionsPerDisplay()
    {
        _tracker.Add(DISPLAY_ID, Connection("a").Object).Should().BeTrue();
        _tracker.Add(DISPLAY_ID, Connection("b").Object).Should().BeTrue();
        _tracker.Add(OTHER_DISPLAY_ID, Connection("c").Object).Should().BeTrue();

        _tracker.GetConnectionIds(DISPLAY_ID).Should().BeEquivalentTo("a", "b");
        _tracker.GetConnectionIds(OTHER_DISPLAY_ID).Should().BeEquivalentTo("c");
    }

    [Fact]
    public void Remove_StopsTrackingOnlyThatConnection()
    {
        _tracker.Add(DISPLAY_ID, Connection("a").Object);
        _tracker.Add(DISPLAY_ID, Connection("b").Object);

        _tracker.Remove(DISPLAY_ID, "a");

        _tracker.GetConnectionIds(DISPLAY_ID).Should().BeEquivalentTo("b");
    }

    [Fact]
    public async Task DisconnectAsync_NotifiesThenAbortsEveryConnectionOfThatDisplay()
    {
        Mock<HubCallerContext> first = Connection("a");
        Mock<HubCallerContext> second = Connection("b");
        Mock<HubCallerContext> other = Connection("c");
        _tracker.Add(DISPLAY_ID, first.Object);
        _tracker.Add(DISPLAY_ID, second.Object);
        _tracker.Add(OTHER_DISPLAY_ID, other.Object);

        int closed = await _tracker.DisconnectAsync(DISPLAY_ID);

        closed.Should().Be(2);
        _notifiedConnectionIds.Should().ContainSingle().Which.Should().BeEquivalentTo("a", "b");
        _mockTargets.Verify(t => t.ReceiveDisplayRevoked(DISPLAY_ID), Times.Once);
        first.Verify(c => c.Abort(), Times.Once);
        second.Verify(c => c.Abort(), Times.Once);
        other.Verify(c => c.Abort(), Times.Never);
        _tracker.GetConnectionIds(DISPLAY_ID).Should().BeEmpty();
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotificationFails_StillAbortsConnections()
    {
        Mock<HubCallerContext> connection = Connection("a");
        _tracker.Add(DISPLAY_ID, connection.Object);
        _mockTargets
            .Setup(t => t.ReceiveDisplayRevoked(DISPLAY_ID))
            .ThrowsAsync(new InvalidOperationException("send failed"));

        await _tracker.DisconnectAsync(DISPLAY_ID);

        connection.Verify(c => c.Abort(), Times.Once);
    }

    [Fact]
    public async Task Add_RightAfterRevocation_IsRefused()
    {
        await _tracker.DisconnectAsync(DISPLAY_ID);

        _tracker.Add(DISPLAY_ID, Connection("late").Object).Should().BeFalse();
        _tracker.GetConnectionIds(DISPLAY_ID).Should().BeEmpty();
        _tracker.Add(OTHER_DISPLAY_ID, Connection("other").Object).Should().BeTrue();
    }

    [Fact]
    public async Task Add_AfterRevocationRetention_IsAcceptedAgain()
    {
        await _tracker.DisconnectAsync(DISPLAY_ID);

        _timeProvider.Advance(TimeSpan.FromSeconds(REVOKED_RETENTION_SECONDS));

        _tracker.Add(DISPLAY_ID, Connection("a").Object).Should().BeTrue();
    }

    private static Mock<HubCallerContext> Connection(string connectionId)
    {
        Mock<HubCallerContext> context = new();
        context.Setup(c => c.ConnectionId).Returns(connectionId);
        return context;
    }
}
