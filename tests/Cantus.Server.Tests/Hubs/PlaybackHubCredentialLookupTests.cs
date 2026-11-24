using Cantus.Core.Interfaces;
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
/// Hub connections resolve their credential to a session by exact session id only.
/// Uses the real token resolver and auth service so the cookie, query and cache
/// paths are all exercised.
/// </summary>
public sealed class PlaybackHubCredentialLookupTests : IDisposable
{
    private const string CONNECTION_ID = "conn-credential-test";
    private const string SESSION_COOKIE = "cantus_session_id";

    private readonly InMemorySessionStore _store = new();
    private readonly Mock<IPlaybackSessionRegistry> _mockRegistry = new();
    private readonly Mock<IHubCallerClients<IPlaybackClient>> _mockClients = new();
    private readonly Mock<IPlaybackClient> _mockCaller = new();
    private readonly Mock<IGroupManager> _mockGroups = new();
    private readonly Mock<HubCallerContext> _mockContext = new();

    public PlaybackHubCredentialLookupTests()
    {
        _mockClients.Setup(c => c.Caller).Returns(_mockCaller.Object);
        _mockContext.Setup(c => c.ConnectionId).Returns(CONNECTION_ID);
        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task OnConnectedAsync_WithSessionIdCookie_JoinsOwnerGroup()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        PlaybackHub hub = CreateHub(WithCookie(seeded.Id));

        await hub.OnConnectedAsync();

        _mockRegistry.Verify(r => r.RegisterConnection(CONNECTION_ID, seeded.Id), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync(CONNECTION_ID, $"user_{seeded.Id}", default), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WithSpotifyUserIdCookie_StaysUnauthenticated()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        PlaybackHub hub = CreateHub(WithCookie(seeded.RequiredSpotifyUserId()));

        await hub.OnConnectedAsync();

        AssertConnectedUnauthenticated();
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("token")]
    public async Task OnConnectedAsync_WithSpotifyUserIdInQueryAfterSessionIsCached_StaysUnauthenticated(
        string queryParameter)
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        PlaybackHub ownerHub = CreateHub(WithCookie(seeded.Id));
        await ownerHub.OnConnectedAsync();
        _mockRegistry.Verify(r => r.RegisterConnection(CONNECTION_ID, seeded.Id), Times.Once);
        ClearRecordedCalls();

        PlaybackHub hub = CreateHub(WithQuery(queryParameter, seeded.RequiredSpotifyUserId()));

        await hub.OnConnectedAsync();

        AssertConnectedUnauthenticated();
    }

    [Fact]
    public async Task SubscribeToUser_WithSessionIdCookie_Subscribes()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        PlaybackHub hub = CreateHub(WithCookie(seeded.Id));

        await hub.SubscribeToUser(seeded.Id);

        _mockRegistry.Verify(r => r.SetConnectionSubscription(CONNECTION_ID, seeded.Id), Times.Once);
    }

    [Fact]
    public async Task SubscribeToUser_WithSpotifyUserIdCookie_DoesNotSubscribe()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        PlaybackHub hub = CreateHub(WithCookie(seeded.RequiredSpotifyUserId()));

        await hub.SubscribeToUser(seeded.Id);

        _mockRegistry.Verify(
            r => r.SetConnectionSubscription(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
        _mockGroups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

    private void AssertConnectedUnauthenticated()
    {
        _mockRegistry.Verify(r => r.RegisterConnection(CONNECTION_ID, null), Times.Once);
        _mockGroups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
        _mockCaller.Verify(
            c => c.ReceiveSessions(It.Is<IReadOnlyList<AuthorizedSessionDto>>(l => l.Count == 0)),
            Times.Once);
    }

    private void ClearRecordedCalls()
    {
        _mockRegistry.Invocations.Clear();
        _mockGroups.Invocations.Clear();
        _mockCaller.Invocations.Clear();
    }

    private PlaybackHub CreateHub(HttpContext httpContext)
    {
        FeatureCollection features = new();
        features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
        _mockContext.Setup(c => c.Features).Returns(features);

        return new PlaybackHub(
            _mockRegistry.Object,
            Mock.Of<ILyricsCacheRepository>(),
            _store.CreateAuthService(),
            Mock.Of<ISpotifyPlayerClient>(),
            _store.CreateCredentialResolver(),
            Mock.Of<IDisplayPairingService>(),
            Mock.Of<IDisplayConnectionTracker>(t => t.Add(It.IsAny<string>(), It.IsAny<HubCallerContext>())),
            Mock.Of<ILocalPlaybackIngest>(),
            NullLogger<PlaybackHub>.Instance)
        {
            Clients = _mockClients.Object,
            Groups = _mockGroups.Object,
            Context = _mockContext.Object
        };
    }

    private static HttpContext WithCookie(string credential)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers.Cookie = $"{SESSION_COOKIE}={credential}";
        return httpContext;
    }

    private static HttpContext WithQuery(string parameter, string credential)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.QueryString = QueryString.Create(parameter, credential);
        return httpContext;
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
