using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Hubs;

public sealed class PlaybackHubTests
{
    private readonly Mock<IPlaybackSessionRegistry> _mockRegistry = new();
    private readonly Mock<ILyricsCacheRepository> _mockLyricsCache = new();
    private readonly Mock<ISpotifyAuthService> _mockAuthService = new();
    private readonly Mock<IHubCallerClients<IPlaybackClient>> _mockClients = new();
    private readonly Mock<IPlaybackClient> _mockCaller = new();
    private readonly Mock<IPlaybackClient> _mockAll = new();
    private readonly Mock<HubCallerContext> _mockContext = new();

    private readonly PlaybackHub _hub;

    public PlaybackHubTests()
    {
        _mockClients.Setup(c => c.Caller).Returns(_mockCaller.Object);
        _mockClients.Setup(c => c.All).Returns(_mockAll.Object);
        _mockContext.Setup(c => c.ConnectionId).Returns("test-conn-id");

        _hub = new PlaybackHub(
            _mockRegistry.Object,
            _mockLyricsCache.Object,
            _mockAuthService.Object,
            NullLogger<PlaybackHub>.Instance)
        {
            Clients = _mockClients.Object,
            Context = _mockContext.Object
        };
    }

    [Fact]
    public async Task SyncClock_ReturnsValidTimestamps()
    {
        long clientSendTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = await _hub.SyncClock(clientSendTime);

        response.Should().NotBeNull();
        response.ClientSendTimeMs.Should().Be(clientSendTime);
        response.ServerReceiveTimeMs.Should().BeGreaterThanOrEqualTo(clientSendTime - 5000);
        response.ServerSendTimeMs.Should().BeGreaterThanOrEqualTo(response.ServerReceiveTimeMs);
    }

    [Fact]
    public async Task SetTrackOffset_SavesToCacheAndBroadcastsToAllClients()
    {
        string trackId = "track-abc";
        int offsetMs = 500;

        await _hub.SetTrackOffset(trackId, offsetMs);

        _mockLyricsCache.Verify(c => c.SetTrackOffsetAsync(trackId, offsetMs, default), Times.Once);
        _mockAll.Verify(c => c.ReceiveTrackOffset(It.Is<TrackOffsetDto>(dto =>
            dto.TrackId == trackId && dto.OffsetMs == offsetMs)), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_RegistersConnectionAndSendsInitialState()
    {
        var snapshot = new UserPlaybackSnapshot(
            "user-1",
            "Alice",
            new PlaybackState
            {
                CurrentTrack = new TrackInfo { Id = "t-1", Title = "Song", Artist = "Artist" },
                IsPlaying = true
            },
            new SyncedLyrics { TrackId = "t-1", Title = "Song", Artist = "Artist", Lines = [] },
            100,
            DateTimeOffset.UtcNow);

        _mockRegistry.Setup(r => r.GetActivePlaybackSnapshot()).Returns(snapshot);
        _mockAuthService.Setup(a => a.GetAllSessionsAsync(default)).ReturnsAsync(new List<UserSession>
        {
            new()
            {
                Id = "user-1",
                SpotifyUserId = "sp-1",
                DisplayName = "Alice",
                AccessToken = "tok",
                RefreshToken = "ref"
            }
        });

        await _hub.OnConnectedAsync();

        _mockRegistry.Verify(r => r.RegisterConnection("test-conn-id"), Times.Once);
        _mockCaller.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Once);
        _mockCaller.Verify(c => c.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Once);
        _mockCaller.Verify(c => c.ReceiveSessions(It.IsAny<IReadOnlyList<AuthorizedSessionDto>>()), Times.Once);
        _mockCaller.Verify(c => c.ReceiveDiagnostics(It.IsAny<DiagnosticsDto>()), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_UnregistersConnection()
    {
        await _hub.OnDisconnectedAsync(null);

        _mockRegistry.Verify(r => r.UnregisterConnection("test-conn-id"), Times.Once);
    }
}
