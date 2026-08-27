using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
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
    private readonly Mock<IPlaybackClient> _mockUserGroup = new();
    private readonly Mock<IGroupManager> _mockGroups = new();
    private readonly Mock<HubCallerContext> _mockContext = new();

    private readonly PlaybackHub _hub;

    public PlaybackHubTests()
    {
        _mockClients.Setup(c => c.Caller).Returns(_mockCaller.Object);
        _mockClients.Setup(c => c.Group("user_user-1")).Returns(_mockUserGroup.Object);
        _mockContext.Setup(c => c.ConnectionId).Returns("test-conn-id");

        _hub = new PlaybackHub(
            _mockRegistry.Object,
            _mockLyricsCache.Object,
            _mockAuthService.Object,
            NullLogger<PlaybackHub>.Instance)
        {
            Clients = _mockClients.Object,
            Groups = _mockGroups.Object,
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
    public async Task SetTrackOffset_WhenAuthenticated_SavesToCacheAndBroadcastsToUserGroup()
    {
        string trackId = "track-abc";
        int offsetMs = 500;
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns("user-1");

        await _hub.SetTrackOffset(trackId, offsetMs);

        _mockLyricsCache.Verify(c => c.SetTrackOffsetAsync(trackId, offsetMs, default), Times.Once);
        _mockUserGroup.Verify(c => c.ReceiveTrackOffset(It.Is<TrackOffsetDto>(dto =>
            dto.TrackId == trackId && dto.OffsetMs == offsetMs)), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WithValidSession_RegistersGroupAndSendsInitialState()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Cookies = new RequestCookieCollection(new Dictionary<string, string>
        {
            ["cantus_session_id"] = "session-123"
        });

        var featureCollection = new FeatureCollection();
        featureCollection.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
        _mockContext.Setup(c => c.Features).Returns(featureCollection);

        var userSession = new UserSession
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok",
            RefreshToken = "ref"
        };

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

        _mockAuthService.Setup(a => a.GetSessionAsync("session-123", default)).ReturnsAsync(userSession);
        _mockRegistry.Setup(r => r.GetUserState("user-1")).Returns(snapshot);

        await _hub.OnConnectedAsync();

        _mockRegistry.Verify(r => r.RegisterConnection("test-conn-id", "user-1"), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync("test-conn-id", "user_user-1", default), Times.Once);
        _mockCaller.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Once);
        _mockCaller.Verify(c => c.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Once);
        _mockCaller.Verify(c => c.ReceiveSessions(It.Is<IReadOnlyList<AuthorizedSessionDto>>(l => l.Count == 1 && l[0].Id == "user-1")), Times.Once);
        _mockCaller.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d => d.ActiveUserId == "user-1")), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenUnauthenticated_RegistersUnauthenticatedConnection()
    {
        var featureCollection = new FeatureCollection();
        _mockContext.Setup(c => c.Features).Returns(featureCollection);

        await _hub.OnConnectedAsync();

        _mockRegistry.Verify(r => r.RegisterConnection("test-conn-id", null), Times.Once);
        _mockCaller.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Never);
        _mockCaller.Verify(c => c.ReceiveSessions(It.Is<IReadOnlyList<AuthorizedSessionDto>>(l => l.Count == 0)), Times.Once);
        _mockCaller.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d => d.ActiveUserId == null)), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_UnregistersConnectionAndRemovesFromGroup()
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns("user-1");

        await _hub.OnDisconnectedAsync(null);

        _mockRegistry.Verify(r => r.UnregisterConnection("test-conn-id"), Times.Once);
        _mockGroups.Verify(g => g.RemoveFromGroupAsync("test-conn-id", "user_user-1", default), Times.Once);
    }

    private sealed class RequestCookieCollection : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _dict;
        public RequestCookieCollection(Dictionary<string, string> dict) => _dict = dict;
        public string? this[string key] => _dict.TryGetValue(key, out var v) ? v : null;
        public int Count => _dict.Count;
        public ICollection<string> Keys => _dict.Keys;
        public bool ContainsKey(string key) => _dict.ContainsKey(key);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _dict.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _dict.GetEnumerator();
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            if (_dict.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
