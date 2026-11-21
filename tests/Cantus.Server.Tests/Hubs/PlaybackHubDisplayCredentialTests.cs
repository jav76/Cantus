using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Hubs;

/// <summary>
/// A Display token gives a hub connection view-only access to its owner's playback.
/// Uses the real credential resolver, Display service and playback registry, so each
/// case runs through the same connect path as production.
/// </summary>
public sealed class PlaybackHubDisplayCredentialTests : IDisposable
{
    private const string CONNECTION_ID = "conn-display-test";
    private const string SESSION_COOKIE = "cantus_session_id";
    private const string DISPLAY_COOKIE = "cantus_display_token";
    private const string TRACK_ID = "track-1";
    private const int SNAPSHOT_OFFSET_MS = 120;
    private const int NEW_OFFSET_MS = 200;
    private const int VOLUME_PERCENT = 50;
    private const long SEEK_POSITION_MS = 1000;
    private const string CLIENT_LOGIN_ID = "client-1";
    private const int REFRESH_BURST_SIZE = 10;

    private readonly InMemorySessionStore _store = new();
    private readonly PlaybackSessionRegistry _registry = new();
    private readonly Mock<ISpotifyPlayerClient> _mockPlayerClient = new();
    private readonly Mock<ILyricsCacheRepository> _mockLyricsCache = new();
    private readonly Mock<IHubCallerClients<IPlaybackClient>> _mockClients = new();
    private readonly Mock<IPlaybackClient> _mockCaller = new();
    private readonly Mock<IGroupManager> _mockGroups = new();
    private readonly Mock<HubCallerContext> _mockContext = new();
    private readonly Mock<IDisplayPairingService> _mockPairings = new();
    private readonly Mock<IDisplayConnectionTracker> _mockDisplayConnections = new();

    public PlaybackHubDisplayCredentialTests()
    {
        _mockClients.Setup(c => c.Caller).Returns(_mockCaller.Object);
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IPlaybackClient>());
        _mockContext.Setup(c => c.ConnectionId).Returns(CONNECTION_ID);
        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        _mockDisplayConnections
            .Setup(t => t.Add(It.IsAny<string>(), It.IsAny<HubCallerContext>()))
            .Returns(true);

        _mockPlayerClient.Setup(p => p.PausePlaybackAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);
        _mockPlayerClient.Setup(p => p.SetVolumeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);
        _mockPlayerClient.Setup(p => p.SeekPlaybackAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);
        _mockPlayerClient.Setup(p => p.SetShuffleAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);
        _mockPlayerClient.Setup(p => p.SetRepeatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    public static TheoryData<string, CredentialKind> MutatingMethodCases()
    {
        string[] methods =
        {
            nameof(PlaybackHub.SendPlayerCommand),
            nameof(PlaybackHub.SetPlayerVolume),
            nameof(PlaybackHub.SeekPlayback),
            nameof(PlaybackHub.SetShuffle),
            nameof(PlaybackHub.SetRepeat),
            nameof(PlaybackHub.SetTrackOffset),
            nameof(PlaybackHub.RegisterClientLogin),
        };

        TheoryData<string, CredentialKind> cases = new();
        foreach (string method in methods)
        {
            cases.Add(method, CredentialKind.Session);
            cases.Add(method, CredentialKind.Display);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(MutatingMethodCases))]
    public async Task MutatingMethod_RunsForSessionAndIsRejectedForDisplay(string method, CredentialKind kind)
    {
        PlaybackHub hub = await ConnectAsync(kind);
        bool allowed = kind == CredentialKind.Session;

        int? result = await InvokeAsync(hub, method);

        WasPerformed(method).Should().Be(allowed);
        if (result is not null)
        {
            PlayerCommandResult expected = allowed ? PlayerCommandResult.Success : PlayerCommandResult.Failed;
            ((PlayerCommandResult)result.Value).Should().Be(expected);
        }
    }

    [Fact]
    public async Task OnConnectedAsync_WithDisplayToken_JoinsOwnersDisplayGroupOnly()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");

        await CreateHub(WithCookie(DISPLAY_COOKIE, display.Token)).OnConnectedAsync();

        _mockGroups.Verify(g => g.AddToGroupAsync(CONNECTION_ID, $"display_{owner.Id}", default), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync(CONNECTION_ID, $"user_{owner.Id}", default), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_WithDisplayToken_ReceivesOwnersSnapshotWithoutSessionId()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        SeedSnapshot(owner);

        await CreateHub(WithCookie(DISPLAY_COOKIE, display.Token)).OnConnectedAsync();

        _mockCaller.Verify(
            c => c.ReceivePlaybackState(It.Is<PlaybackStateDto>(s =>
                s.CurrentTrack!.Id == TRACK_ID && s.ActiveUserId == null && s.ActiveUserDisplayName == "Owner")),
            Times.Once);
        _mockCaller.Verify(c => c.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == TRACK_ID)), Times.Once);
        _mockCaller.Verify(
            c => c.ReceiveTrackOffset(It.Is<TrackOffsetDto>(o =>
                o.TrackId == TRACK_ID && o.OffsetMs == SNAPSHOT_OFFSET_MS)),
            Times.Once);
        _mockCaller.Verify(
            c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
                d.ActiveUserId == null && d.ActiveUserName == "Owner")),
            Times.Once);
        _mockCaller.Verify(c => c.ReceiveSessions(It.IsAny<IReadOnlyList<AuthorizedSessionDto>>()), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_WithDisplayToken_TellsOwnersSessionsTheDisplaysChanged()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        Mock<IPlaybackClient> ownerGroup = new();
        Mock<IPlaybackClient> displayGroup = new();
        _mockClients.Setup(c => c.Group($"user_{owner.Id}")).Returns(ownerGroup.Object);
        _mockClients.Setup(c => c.Group($"display_{owner.Id}")).Returns(displayGroup.Object);

        await CreateHub(WithCookie(DISPLAY_COOKIE, display.Token)).OnConnectedAsync();

        ownerGroup.Verify(c => c.ReceiveDisplaysChanged(), Times.Once);
        displayGroup.Verify(c => c.ReceiveDisplaysChanged(), Times.Never);
        _mockCaller.Verify(c => c.ReceiveDisplaysChanged(), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_WithSession_DoesNotReportDisplaysChanged()
    {
        Mock<IPlaybackClient> group = new();
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(group.Object);

        await ConnectAsync(CredentialKind.Session);

        group.Verify(c => c.ReceiveDisplaysChanged(), Times.Never);
        _mockCaller.Verify(c => c.ReceiveDisplaysChanged(), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_WithDisplayToken_CountsAsViewerOfOwner()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");

        await CreateHub(WithCookie(DISPLAY_COOKIE, display.Token)).OnConnectedAsync();

        _registry.HasConnectedClients.Should().BeTrue();
        _registry.GetActiveUserIdsWithConnectedClients().Should().Contain(owner.Id);
        _registry.IsUserVisible(owner.Id).Should().BeTrue();
    }

    [Fact]
    public async Task OnDisconnectedAsync_ForDisplay_StopsCountingAsViewer()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        PlaybackHub hub = CreateHub(WithCookie(DISPLAY_COOKIE, display.Token));
        await hub.OnConnectedAsync();

        await hub.OnDisconnectedAsync(null);

        _registry.GetActiveUserIdsWithConnectedClients().Should().NotContain(owner.Id);
        _mockGroups.Verify(g => g.RemoveFromGroupAsync(CONNECTION_ID, $"display_{owner.Id}", default), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WithDisplayToken_TracksConnectionUnderItsDisplay()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");

        await CreateHub(WithCookie(DISPLAY_COOKIE, display.Token)).OnConnectedAsync();

        _mockDisplayConnections.Verify(t => t.Add(display.Display.Id, _mockContext.Object), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_ForJustRevokedDisplay_AbortsWithoutJoiningGroup()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        _mockDisplayConnections.Setup(t => t.Add(display.Display.Id, It.IsAny<HubCallerContext>())).Returns(false);

        await CreateHub(WithCookie(DISPLAY_COOKIE, display.Token)).OnConnectedAsync();

        _mockContext.Verify(c => c.Abort(), Times.Once);
        _mockGroups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
        _mockCaller.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Never);
    }

    [Fact]
    public async Task OnConnectedAsync_WithSession_IsNotTrackedAsDisplay()
    {
        await ConnectAsync(CredentialKind.Session);

        _mockDisplayConnections.Verify(
            t => t.Add(It.IsAny<string>(), It.IsAny<HubCallerContext>()),
            Times.Never);
    }

    [Fact]
    public async Task OnDisconnectedAsync_ForDisplay_StopsTrackingConnection()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        PlaybackHub hub = CreateHub(WithCookie(DISPLAY_COOKIE, display.Token));
        await hub.OnConnectedAsync();

        await hub.OnDisconnectedAsync(null);

        _mockDisplayConnections.Verify(t => t.Remove(display.Display.Id, CONNECTION_ID), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_ForAnonymousConnection_CancelsItsPendingPairing()
    {
        PlaybackHub hub = CreateHub(new DefaultHttpContext());
        await hub.OnConnectedAsync();

        await hub.OnDisconnectedAsync(null);

        _mockPairings.Verify(p => p.Cancel(CONNECTION_ID), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WithRevokedDisplayToken_StaysUnauthenticated()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        await _store.CreateDisplayService().RevokeAsync(owner.Id, display.Display.Id);

        await CreateHub(WithCookie(DISPLAY_COOKIE, display.Token)).OnConnectedAsync();

        _registry.GetActiveUserIdsWithConnectedClients().Should().BeEmpty();
        _mockGroups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

    [Fact]
    public async Task SyncClock_FromDisplayConnection_ReturnsServerTimestamps()
    {
        PlaybackHub hub = await ConnectAsync(CredentialKind.Display);
        long clientSendTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        ClockSyncResponse response = await hub.SyncClock(clientSendTimeMs);

        response.ClientSendTimeMs.Should().Be(clientSendTimeMs);
        response.ServerSendTimeMs.Should().BeGreaterThanOrEqualTo(response.ServerReceiveTimeMs);
    }

    [Fact]
    public async Task SubscribeToUser_FromDisplayConnection_KeepsOwnerSubscription()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        UserSessionEntity other = await _store.SeedSessionAsync("Other");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        PlaybackHub hub = CreateHub(WithCookie(DISPLAY_COOKIE, display.Token));
        await hub.OnConnectedAsync();

        await hub.SubscribeToUser(other.Id);

        _registry.GetConnectionSubscription(CONNECTION_ID).Should().Be(owner.Id);
        _mockGroups.Verify(g => g.AddToGroupAsync(CONNECTION_ID, $"user_{other.Id}", default), Times.Never);
    }

    [Theory]
    [InlineData(CredentialKind.Session, REFRESH_BURST_SIZE)]
    [InlineData(CredentialKind.Display, 1)]
    public async Task RefreshPlayback_InABurst_ForcesOnePollForDisplayOnly(CredentialKind kind, int expectedPolls)
    {
        PlaybackHub hub = await ConnectAsync(kind);
        int forcedPolls = 0;
        _registry.OnUserActivityRequested += (_, _) => forcedPolls++;

        for (int i = 0; i < REFRESH_BURST_SIZE; i++)
        {
            await hub.RefreshPlayback();
        }

        forcedPolls.Should().Be(expectedPolls);
    }

    [Theory]
    [InlineData(CredentialKind.Session, REFRESH_BURST_SIZE)]
    [InlineData(CredentialKind.Display, 1)]
    public async Task ReportClientVisibility_InABurst_ForcesOnePollForDisplayOnly(
        CredentialKind kind,
        int expectedPolls)
    {
        PlaybackHub hub = await ConnectAsync(kind);
        int forcedPolls = 0;
        _registry.OnUserActivityRequested += (_, _) => forcedPolls++;

        for (int i = 0; i < REFRESH_BURST_SIZE; i++)
        {
            await hub.ReportClientVisibility(true);
        }

        forcedPolls.Should().Be(expectedPolls);
    }

    private async Task<PlaybackHub> ConnectAsync(CredentialKind kind)
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        SeedSnapshot(owner);

        HttpContext httpContext;
        if (kind == CredentialKind.Session)
        {
            httpContext = WithCookie(SESSION_COOKIE, owner.Id);
        }
        else
        {
            CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
            httpContext = WithCookie(DISPLAY_COOKIE, display.Token);
        }

        PlaybackHub hub = CreateHub(httpContext);
        await hub.OnConnectedAsync();
        _registry.GetConnectionSubscription(CONNECTION_ID).Should().Be(owner.Id);
        return hub;
    }

    private static async Task<int?> InvokeAsync(PlaybackHub hub, string method)
    {
        switch (method)
        {
            case nameof(PlaybackHub.SendPlayerCommand):
                return await hub.SendPlayerCommand("pause");
            case nameof(PlaybackHub.SetPlayerVolume):
                return await hub.SetPlayerVolume(VOLUME_PERCENT);
            case nameof(PlaybackHub.SeekPlayback):
                return await hub.SeekPlayback(SEEK_POSITION_MS);
            case nameof(PlaybackHub.SetShuffle):
                return await hub.SetShuffle(true);
            case nameof(PlaybackHub.SetRepeat):
                return await hub.SetRepeat("track");
            case nameof(PlaybackHub.SetTrackOffset):
                await hub.SetTrackOffset(TRACK_ID, NEW_OFFSET_MS);
                return null;
            case nameof(PlaybackHub.RegisterClientLogin):
                await hub.RegisterClientLogin(CLIENT_LOGIN_ID);
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown hub method.");
        }
    }

    private bool WasPerformed(string method)
    {
        return method switch
        {
            nameof(PlaybackHub.SendPlayerCommand) => _mockPlayerClient.Invocations
                .Any(i => i.Method.Name == nameof(ISpotifyPlayerClient.PausePlaybackAsync)),
            nameof(PlaybackHub.SetPlayerVolume) => _mockPlayerClient.Invocations
                .Any(i => i.Method.Name == nameof(ISpotifyPlayerClient.SetVolumeAsync)),
            nameof(PlaybackHub.SeekPlayback) => _mockPlayerClient.Invocations
                .Any(i => i.Method.Name == nameof(ISpotifyPlayerClient.SeekPlaybackAsync)),
            nameof(PlaybackHub.SetShuffle) => _mockPlayerClient.Invocations
                .Any(i => i.Method.Name == nameof(ISpotifyPlayerClient.SetShuffleAsync)),
            nameof(PlaybackHub.SetRepeat) => _mockPlayerClient.Invocations
                .Any(i => i.Method.Name == nameof(ISpotifyPlayerClient.SetRepeatAsync)),
            nameof(PlaybackHub.SetTrackOffset) => _mockLyricsCache.Invocations
                .Any(i => i.Method.Name == nameof(ILyricsCacheRepository.SetTrackOffsetAsync)),
            nameof(PlaybackHub.RegisterClientLogin) => _mockGroups.Invocations
                .Any(i => i.Arguments.Contains($"client_{CLIENT_LOGIN_ID}")),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown hub method.")
        };
    }

    private void SeedSnapshot(UserSessionEntity owner)
    {
        _registry.UpdateUserState(
            owner.Id,
            owner.DisplayName,
            new PlaybackState
            {
                CurrentTrack = new TrackInfo { Id = TRACK_ID, Title = "Song", Artist = "Artist" },
                IsPlaying = true
            },
            new SyncedLyrics { TrackId = TRACK_ID, Title = "Song", Artist = "Artist", Lines = [] },
            SNAPSHOT_OFFSET_MS);
    }

    private PlaybackHub CreateHub(HttpContext httpContext)
    {
        FeatureCollection features = new();
        features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
        _mockContext.Setup(c => c.Features).Returns(features);

        return new PlaybackHub(
            _registry,
            _mockLyricsCache.Object,
            _store.CreateAuthService(),
            _mockPlayerClient.Object,
            _store.CreateCredentialResolver(),
            _mockPairings.Object,
            _mockDisplayConnections.Object,
            NullLogger<PlaybackHub>.Instance)
        {
            Clients = _mockClients.Object,
            Groups = _mockGroups.Object,
            Context = _mockContext.Object
        };
    }

    private static HttpContext WithCookie(string cookie, string value)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers.Cookie = $"{cookie}={value}";
        return httpContext;
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
