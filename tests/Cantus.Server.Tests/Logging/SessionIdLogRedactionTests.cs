using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Server.BackgroundServices;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SpotifyAPI.Web;

namespace Cantus.Server.Tests.Logging;

/// <summary>
/// A Session id is the session cookie value, so it must never reach a log line, including the
/// structured values and exception text that the LogEntries table stores.
/// </summary>
public sealed class SessionIdLogRedactionTests
{
    private const string SESSION_ID = "a3f1c9e0b7d24e6f8c5a1b2d3e4f5a6b";
    private const string SPOTIFY_USER_ID = "spotify-user-7";
    private const string ACCESS_TOKEN = "access-token-secret";
    private const int SIGNAL_TIMEOUT_MS = 10_000;

    private static readonly string SessionRef = SessionLogRef.For(SESSION_ID);

    private static UserSession CreateSession(string accessToken = ACCESS_TOKEN) => new()
    {
        Id = SESSION_ID,
        SpotifyUserId = SPOTIFY_USER_ID,
        DisplayName = "Alice",
        AccessToken = accessToken,
        RefreshToken = "refresh-token-secret"
    };

    private static void ShouldNotLeakSecrets(CapturingLogger<PlaybackHub> logger) => ShouldNotLeak(logger.AllText);

    private static void ShouldNotLeak(string logText)
    {
        logText.Should().NotContain(SESSION_ID);
        logText.Should().NotContain(ACCESS_TOKEN);
    }

    private static async Task WaitForAsync(Func<bool> condition, string description)
    {
        using CancellationTokenSource timeout = new(SIGNAL_TIMEOUT_MS);
        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for {description}.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class HubHarness
    {
        public Mock<IPlaybackSessionRegistry> Registry { get; } = new();
        public Mock<ISpotifyAuthService> AuthService { get; } = new();
        public Mock<ISpotifyPlayerClient> PlayerClient { get; } = new();
        public Mock<ISessionTokenResolver> SessionResolver { get; } = new();
        public Mock<HubCallerContext> Context { get; } = new();
        public CapturingLogger<PlaybackHub> Logger { get; } = new();
        public PlaybackHub Hub { get; }

        public HubHarness()
        {
            Mock<IHubCallerClients<IPlaybackClient>> clients = new() { DefaultValue = DefaultValue.Mock };
            Context.Setup(c => c.ConnectionId).Returns("conn-1");
            Context.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

            DefaultHttpContext httpContext = new();
            FeatureCollection features = new();
            features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
            Context.Setup(c => c.Features).Returns(features);

            SessionResolver.Setup(s => s.ResolveSessionId(It.IsAny<HttpContext>())).Returns(SESSION_ID);
            AuthService.Setup(a => a.GetSessionByIdAsync(SESSION_ID, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSession());
            AuthService.Setup(a => a.GetSessionForUserAsync(SESSION_ID, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSession());

            Hub = new PlaybackHub(
                Registry.Object,
                Mock.Of<ILyricsCacheRepository>(),
                AuthService.Object,
                PlayerClient.Object,
                new CredentialResolver(SessionResolver.Object, AuthService.Object, Mock.Of<IDisplayService>()),
                Mock.Of<IDisplayPairingService>(),
                Mock.Of<IDisplayConnectionTracker>(t => t.Add(It.IsAny<string>(), It.IsAny<HubCallerContext>())),
                Logger)
            {
                Clients = clients.Object,
                Groups = Mock.Of<IGroupManager>(),
                Context = Context.Object
            };
        }
    }

    [Fact]
    public async Task HubConnect_LogsTheSessionRefInsteadOfTheSessionId()
    {
        HubHarness harness = new();

        await harness.Hub.OnConnectedAsync();

        harness.Logger.AllText.Should().Contain("Client connected to PlaybackHub");
        harness.Logger.AllText.Should().Contain(SessionRef);
        ShouldNotLeakSecrets(harness.Logger);
    }

    [Fact]
    public async Task HubPlayerCommandAndFailedFollowUpPoll_DoNotLogTheSessionId()
    {
        HubHarness harness = new();
        harness.Registry.Setup(r => r.GetConnectionSubscription("conn-1")).Returns(SESSION_ID);
        harness.PlayerClient.Setup(p => p.PausePlaybackAsync(ACCESS_TOKEN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);

        int requests = 0;
        harness.Registry.Setup(r => r.RequestUserActivity(SESSION_ID)).Callback(() =>
        {
            if (Interlocked.Increment(ref requests) > 1)
            {
                throw new InvalidOperationException("follow-up failed");
            }
        });

        await harness.Hub.SendPlayerCommand("pause");
        await WaitForAsync(
            () => harness.Logger.AllText.Contains("Follow-up playback poll failed"),
            "the follow-up poll failure log");

        harness.Logger.AllText.Should().Contain("Player command pause requested");
        harness.Logger.AllText.Should().Contain(SessionRef);
        ShouldNotLeakSecrets(harness.Logger);
    }

    private sealed class MonitorHarness
    {
        public Mock<ISpotifyAuthService> AuthService { get; } = new();
        public Mock<ISpotifyPlayerClient> SpotifyClient { get; } = new();
        public CapturingLogger<ActiveUsersPlaybackMonitor> Logger { get; } = new();
        public ActiveUsersPlaybackMonitor Monitor { get; }

        public MonitorHarness()
        {
            Mock<IServiceProvider> services = new();
            services.Setup(s => s.GetService(typeof(ISpotifyAuthService))).Returns(AuthService.Object);
            services.Setup(s => s.GetService(typeof(ISpotifyPlayerClient))).Returns(SpotifyClient.Object);
            services.Setup(s => s.GetService(typeof(ILyricsProvider))).Returns(Mock.Of<ILyricsProvider>());
            services.Setup(s => s.GetService(typeof(ILyricsCacheRepository))).Returns(Mock.Of<ILyricsCacheRepository>());
            Mock<IServiceScope> scope = new();
            scope.Setup(s => s.ServiceProvider).Returns(services.Object);
            Mock<IServiceScopeFactory> scopeFactory = new();
            scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

            Mock<IPlaybackSessionRegistry> registry = new();
            registry.Setup(r => r.HasConnectedClients).Returns(true);
            registry.Setup(r => r.GetActiveUserIdsWithConnectedClients()).Returns(new HashSet<string> { SESSION_ID });
            registry.Setup(r => r.IsUserVisible(It.IsAny<string>())).Returns(true);

            Mock<IHubContext<PlaybackHub, IPlaybackClient>> hubContext = new();
            Mock<IHubClients<IPlaybackClient>> clients = new() { DefaultValue = DefaultValue.Mock };
            hubContext.Setup(h => h.Clients).Returns(clients.Object);

            Monitor = new ActiveUsersPlaybackMonitor(
                scopeFactory.Object,
                registry.Object,
                hubContext.Object,
                Options.Create(new PlaybackPollerOptions { PausedPollIntervalMs = 50, IdlePollIntervalMs = 50 }),
                Logger);
        }

        public async Task RunUntilLoggedAsync(string fragment)
        {
            await Monitor.StartAsync(CancellationToken.None);
            try
            {
                await WaitForAsync(() => Logger.AllText.Contains(fragment), $"a log containing '{fragment}'");
            }
            finally
            {
                await Monitor.StopAsync(CancellationToken.None);
            }
        }
    }

    private static Mock<SpotifyAPI.Web.Http.IResponse> CreateResponse(Dictionary<string, string> headers)
    {
        Mock<SpotifyAPI.Web.Http.IResponse> response = new();
        response.Setup(r => r.Headers).Returns(headers);
        return response;
    }

    [Fact]
    public async Task MonitorRateLimit_LogsTheSpotifyUserAndSessionRefOnly()
    {
        MonitorHarness harness = new();
        harness.AuthService.Setup(a => a.GetSessionForUserAsync(SESSION_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSession());
        harness.SpotifyClient.Setup(s => s.GetCurrentPlaybackAsync(ACCESS_TOKEN, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new APITooManyRequestsException(
                CreateResponse(new Dictionary<string, string> { ["Retry-After"] = "120" }).Object));

        await harness.RunUntilLoggedAsync("rate limit hit");

        harness.Logger.AllText.Should().Contain(SPOTIFY_USER_ID);
        harness.Logger.AllText.Should().Contain(SessionRef);
        ShouldNotLeak(harness.Logger.AllText);
    }

    [Fact]
    public async Task MonitorTokenExpiryAndRefreshFailure_DoNotLogTheSessionId()
    {
        MonitorHarness harness = new();
        harness.AuthService.Setup(a => a.GetSessionForUserAsync(SESSION_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSession());
        harness.SpotifyClient.Setup(s => s.GetCurrentPlaybackAsync(ACCESS_TOKEN, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new APIUnauthorizedException(CreateResponse(new Dictionary<string, string>()).Object));
        harness.AuthService.Setup(a => a.RefreshTokenAsync(SESSION_ID, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refresh failed"));

        await harness.RunUntilLoggedAsync("Failed to refresh token");

        harness.Logger.AllText.Should().Contain("Spotify token expired");
        harness.Logger.AllText.Should().Contain(SPOTIFY_USER_ID);
        ShouldNotLeak(harness.Logger.AllText);
    }

    [Fact]
    public async Task MonitorPollingError_LogsTheSessionRefInsteadOfTheSessionId()
    {
        MonitorHarness harness = new();
        harness.AuthService.Setup(a => a.GetSessionForUserAsync(SESSION_ID, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        await harness.RunUntilLoggedAsync("Error polling Spotify playback");

        harness.Logger.AllText.Should().Contain(SessionRef);
        ShouldNotLeak(harness.Logger.AllText);
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
