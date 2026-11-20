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
    private readonly Mock<ISpotifyPlayerClient> _mockPlayerClient = new();
    private readonly Mock<ISessionTokenResolver> _mockSessionResolver = new();
    private readonly Mock<IGroupManager> _mockGroups = new();
    private readonly Mock<HubCallerContext> _mockContext = new();

    private readonly PlaybackHub _hub;

    public PlaybackHubTests()
    {
        _mockClients.Setup(c => c.Caller).Returns(_mockCaller.Object);
        _mockClients.Setup(c => c.Group("user_user-1")).Returns(_mockUserGroup.Object);
        _mockClients.Setup(c => c.Group("display_user-1")).Returns(Mock.Of<IPlaybackClient>());
        _mockContext.Setup(c => c.ConnectionId).Returns("test-conn-id");
        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

        _hub = new PlaybackHub(
            _mockRegistry.Object,
            _mockLyricsCache.Object,
            _mockAuthService.Object,
            _mockPlayerClient.Object,
            new CredentialResolver(
                _mockSessionResolver.Object,
                _mockAuthService.Object,
                Mock.Of<IDisplayService>()),
            Mock.Of<IDisplayPairingService>(),
            Mock.Of<IDisplayConnectionTracker>(t => t.Add(It.IsAny<string>(), It.IsAny<HubCallerContext>())),
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

        ClockSyncResponse response = await _hub.SyncClock(clientSendTime);

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
        DefaultHttpContext httpContext = new();
        httpContext.Request.Cookies = new RequestCookieCollection(new Dictionary<string, string>
        {
            ["cantus_session_id"] = "session-123"
        });

        FeatureCollection featureCollection = new();
        featureCollection.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
        _mockContext.Setup(c => c.Features).Returns(featureCollection);

        UserSession userSession = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok",
            RefreshToken = "ref"
        };

        UserPlaybackSnapshot snapshot = new(
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

        _mockSessionResolver.Setup(s => s.ResolveSessionId(It.IsAny<HttpContext>())).Returns("session-123");
        _mockAuthService.Setup(a => a.GetSessionByIdAsync("session-123", default)).ReturnsAsync(userSession);
        _mockRegistry.Setup(r => r.GetUserState("user-1")).Returns(snapshot);

        await _hub.OnConnectedAsync();

        _mockRegistry.Verify(r => r.RegisterConnection("test-conn-id", "user-1"), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync("test-conn-id", "user_user-1", default), Times.Once);
        _mockCaller.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Once);
        _mockCaller.Verify(c => c.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Once);
        _mockCaller.Verify(
            c => c.ReceiveSessions(
                It.Is<IReadOnlyList<AuthorizedSessionDto>>(l => l.Count == 1 && l[0].Id == "user-1")),
            Times.Once);
        _mockCaller.Verify(
            c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d => d.ActiveUserId == "user-1")),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenUnauthenticated_RegistersUnauthenticatedConnection()
    {
        FeatureCollection featureCollection = new();
        _mockContext.Setup(c => c.Features).Returns(featureCollection);
        _mockAuthService.Setup(a => a.GetAllSessionsAsync(default)).ReturnsAsync(new List<UserSession>());

        await _hub.OnConnectedAsync();

        _mockRegistry.Verify(r => r.RegisterConnection("test-conn-id", null), Times.Once);
        _mockCaller.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Never);
        _mockCaller.Verify(
            c => c.ReceiveSessions(It.Is<IReadOnlyList<AuthorizedSessionDto>>(l => l.Count == 0)),
            Times.Once);
        _mockCaller.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d => d.ActiveUserId == null)), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenNoCookieButSessionExists_KeepsConnectionUnauthenticated()
    {
        FeatureCollection featureCollection = new();
        _mockContext.Setup(c => c.Features).Returns(featureCollection);

        UserSession userSession = new()
        {
            Id = "user-default",
            SpotifyUserId = "sp-default",
            DisplayName = "Bob",
            AccessToken = "tok",
            RefreshToken = "ref"
        };

        _mockAuthService.Setup(a => a.GetAllSessionsAsync(default))
            .ReturnsAsync(new List<UserSession> { userSession });

        await _hub.OnConnectedAsync();

        _mockRegistry.Verify(r => r.RegisterConnection("test-conn-id", null), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        _mockCaller.Verify(
            c => c.ReceiveSessions(It.Is<IReadOnlyList<AuthorizedSessionDto>>(l => l.Count == 0)),
            Times.Once);
        _mockCaller.Verify(
            c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d => d.ActiveUserId == null)),
            Times.Once);
    }

    [Fact]
    public async Task RegisterClientLogin_WhenCalled_AddsConnectionToClientGroup()
    {
        await _hub.RegisterClientLogin("client-xyz");

        _mockGroups.Verify(g => g.AddToGroupAsync("test-conn-id", "client_client-xyz", default), Times.Once);
    }

    [Fact]
    public async Task SubscribeToUser_WhenAuthenticated_UpdatesSubscriptionAndJoinsGroup()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Cookies = new RequestCookieCollection(new Dictionary<string, string>
        {
            ["cantus_session_id"] = "user-2"
        });

        FeatureCollection featureCollection = new();
        featureCollection.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
        _mockContext.Setup(c => c.Features).Returns(featureCollection);

        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns((string?)null);
        _mockSessionResolver.Setup(s => s.ResolveSessionId(It.IsAny<HttpContext>())).Returns("user-2");
        _mockAuthService.Setup(a => a.GetSessionByIdAsync("user-2", default)).ReturnsAsync(new UserSession
        {
            Id = "user-2",
            SpotifyUserId = "sp-2",
            DisplayName = "Charlie",
            AccessToken = "tok",
            RefreshToken = "ref"
        });

        await _hub.SubscribeToUser("user-2");

        _mockRegistry.Verify(r => r.SetConnectionSubscription("test-conn-id", "user-2"), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync("test-conn-id", "user_user-2", default), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_UnregistersConnectionAndRemovesFromGroup()
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns("user-1");

        await _hub.OnDisconnectedAsync(null);

        _mockRegistry.Verify(r => r.UnregisterConnection("test-conn-id"), Times.Once);
        _mockGroups.Verify(g => g.RemoveFromGroupAsync("test-conn-id", "user_user-1", default), Times.Once);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("next")]
    [InlineData("previous")]
    public async Task SendPlayerCommand_WhenSubscribedWithSession_ExecutesCommandAndRequestsRefresh(string command)
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns("user-1");
        _mockAuthService.Setup(a => a.GetSessionForUserAsync("user-1", default)).ReturnsAsync(new UserSession
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "access-token-1",
            RefreshToken = "ref"
        });
        _mockPlayerClient.Setup(p => p.PausePlaybackAsync("access-token-1", default)).ReturnsAsync(PlayerCommandResult.Success);
        _mockPlayerClient.Setup(p => p.ResumePlaybackAsync("access-token-1", default)).ReturnsAsync(PlayerCommandResult.Success);
        _mockPlayerClient.Setup(p => p.SkipToNextAsync("access-token-1", default)).ReturnsAsync(PlayerCommandResult.Success);
        _mockPlayerClient.Setup(p => p.SkipToPreviousAsync("access-token-1", default)).ReturnsAsync(PlayerCommandResult.Success);

        int result = await _hub.SendPlayerCommand(command);

        result.Should().Be((int)PlayerCommandResult.Success);
        // AtLeastOnce: delayed follow-up polls may also fire while the test runs.
        _mockRegistry.Verify(r => r.RequestUserActivity("user-1"), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SendPlayerCommand_WhenNotSubscribed_FailsWithoutCallingSpotify()
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns((string?)null);

        int result = await _hub.SendPlayerCommand("pause");

        result.Should().Be((int)PlayerCommandResult.Failed);
        _mockPlayerClient.Verify(
            p => p.PausePlaybackAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRegistry.Verify(r => r.RequestUserActivity(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(PlayerCommandResult.MissingPermissions)]
    [InlineData(PlayerCommandResult.NoActiveDevice)]
    [InlineData(PlayerCommandResult.Failed)]
    public async Task SendPlayerCommand_WhenSpotifyRejectsCommand_PropagatesResultWithoutRefresh(
        PlayerCommandResult rejection)
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns("user-1");
        _mockAuthService.Setup(a => a.GetSessionForUserAsync("user-1", default)).ReturnsAsync(new UserSession
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "access-token-1",
            RefreshToken = "ref"
        });
        _mockPlayerClient.Setup(p => p.PausePlaybackAsync("access-token-1", default)).ReturnsAsync(rejection);

        int result = await _hub.SendPlayerCommand("pause");

        result.Should().Be((int)rejection);
        _mockRegistry.Verify(r => r.RequestUserActivity(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendPlayerCommand_WithUnknownCommand_Fails()
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns("user-1");
        _mockAuthService.Setup(a => a.GetSessionForUserAsync("user-1", default)).ReturnsAsync(new UserSession
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "access-token-1",
            RefreshToken = "ref"
        });

        int result = await _hub.SendPlayerCommand("shuffle");

        result.Should().Be((int)PlayerCommandResult.Failed);
        _mockRegistry.Verify(r => r.RequestUserActivity(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetPlayerVolume_WhenSubscribedWithSession_PassesValueAndRequestsRefresh()
    {
        ArrangeSubscribedSession();
        _mockPlayerClient.Setup(p => p.SetVolumeAsync("access-token-1", 65, default))
            .ReturnsAsync(PlayerCommandResult.Success);

        int result = await _hub.SetPlayerVolume(65);

        result.Should().Be((int)PlayerCommandResult.Success);
        _mockPlayerClient.Verify(p => p.SetVolumeAsync("access-token-1", 65, default), Times.Once);
        _mockRegistry.Verify(r => r.RequestUserActivity("user-1"), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SeekPlayback_WhenSpotifyRejects_PropagatesResultWithoutRefresh()
    {
        ArrangeSubscribedSession();
        _mockPlayerClient.Setup(p => p.SeekPlaybackAsync("access-token-1", 30000, default))
            .ReturnsAsync(PlayerCommandResult.NoActiveDevice);

        int result = await _hub.SeekPlayback(30000);

        result.Should().Be((int)PlayerCommandResult.NoActiveDevice);
        _mockRegistry.Verify(r => r.RequestUserActivity(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetShuffle_WhenNotSubscribed_FailsWithoutCallingSpotify()
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns((string?)null);

        int result = await _hub.SetShuffle(true);

        result.Should().Be((int)PlayerCommandResult.Failed);
        _mockPlayerClient.Verify(
            p => p.SetShuffleAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetRepeat_WhenSubscribedWithSession_PassesModeThrough()
    {
        ArrangeSubscribedSession();
        _mockPlayerClient.Setup(p => p.SetRepeatAsync("access-token-1", "track", default))
            .ReturnsAsync(PlayerCommandResult.Success);

        int result = await _hub.SetRepeat("track");

        result.Should().Be((int)PlayerCommandResult.Success);
        _mockPlayerClient.Verify(p => p.SetRepeatAsync("access-token-1", "track", default), Times.Once);
    }

    private void ArrangeSubscribedSession()
    {
        _mockRegistry.Setup(r => r.GetConnectionSubscription("test-conn-id")).Returns("user-1");
        _mockAuthService.Setup(a => a.GetSessionForUserAsync("user-1", default)).ReturnsAsync(new UserSession
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "access-token-1",
            RefreshToken = "ref"
        });
    }

    private sealed class RequestCookieCollection : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _dict;
        public RequestCookieCollection(Dictionary<string, string> dict) => _dict = dict;
        public string? this[string key] => _dict.TryGetValue(key, out string? v) ? v : null;
        public int Count => _dict.Count;
        public ICollection<string> Keys => _dict.Keys;
        public bool ContainsKey(string key) => _dict.ContainsKey(key);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _dict.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _dict.GetEnumerator();
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            if (_dict.TryGetValue(key, out string? v))
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
