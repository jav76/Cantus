using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.BackgroundServices;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SpotifyAPI.Web;
using Xunit;

namespace Cantus.Server.Tests.BackgroundServices;

public sealed class ActiveUsersPlaybackMonitorTests
{
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();
    private readonly Mock<IServiceScope> _mockScope = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();
    private readonly Mock<IPlaybackSessionRegistry> _mockRegistry = new();
    private readonly Mock<IHubContext<PlaybackHub, IPlaybackClient>> _mockHubContext = new();
    private readonly Mock<IHubClients<IPlaybackClient>> _mockClients = new();
    private readonly Mock<IPlaybackClient> _mockUser1Group = new();
    private readonly Mock<IPlaybackClient> _mockUser2Group = new();
    private readonly Mock<IPlaybackClient> _mockAll = new();

    private readonly Mock<ISpotifyAuthService> _mockAuthService = new();
    private readonly Mock<ISpotifyPlayerClient> _mockSpotifyClient = new();
    private readonly Mock<ILyricsProvider> _mockLyricsProvider = new();
    private readonly Mock<ILyricsCacheRepository> _mockLyricsCache = new();

    private readonly ActiveUsersPlaybackMonitor _monitor;

    public ActiveUsersPlaybackMonitorTests()
    {
        _mockScopeFactory.Setup(s => s.CreateScope()).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);

        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ISpotifyAuthService)))
            .Returns(_mockAuthService.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ISpotifyPlayerClient)))
            .Returns(_mockSpotifyClient.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILyricsProvider)))
            .Returns(_mockLyricsProvider.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILyricsCacheRepository)))
            .Returns(_mockLyricsCache.Object);

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.All).Returns(_mockAll.Object);
        _mockClients.Setup(c => c.Group("user_user-1")).Returns(_mockUser1Group.Object);
        _mockClients.Setup(c => c.Group("user_user-2")).Returns(_mockUser2Group.Object);

        _mockRegistry.Setup(r => r.IsUserVisible(It.IsAny<string>())).Returns(true);

        IOptions<PlaybackPollerOptions> options = Options.Create(new PlaybackPollerOptions
        {
            ActivePollIntervalMs = 50,
            ApproachingEndPollIntervalMs = 50,
            ImminentEndPollIntervalMs = 50,
            PausedPollIntervalMs = 50,
            IdlePollIntervalMs = 50,
            DiagnosticsBroadcastIntervalMs = 50
        });

        _monitor = new ActiveUsersPlaybackMonitor(
            _mockScopeFactory.Object,
            _mockRegistry.Object,
            _mockHubContext.Object,
            options,
            NullLogger<ActiveUsersPlaybackMonitor>.Instance);
    }

    // The monitor is a background loop, so these tests used to sleep a fixed
    // interval and hope an iteration had completed. On a loaded CI runner it
    // sometimes had not, producing failures unrelated to the code under test.
    // Each test now waits for the specific broadcast it asserts on.
    private const int SIGNAL_TIMEOUT_MS = 10_000;
    private const int SAFETY_STOP_TIMEOUT_MS = 30_000;

    private static TaskCompletionSource CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Runs <paramref name="monitor"/> until every signal fires, then stops it.
    /// Waiting on the signal means a fast machine finishes immediately and a
    /// slow one still succeeds, while a genuine regression fails with a named
    /// timeout instead of a bare "invocation never performed".
    /// </summary>
    private static async Task RunUntilAsync(
        IHostedService monitor,
        string description,
        params Task[] signals)
    {
        using CancellationTokenSource safety = new(SAFETY_STOP_TIMEOUT_MS);
        await monitor.StartAsync(safety.Token);

        try
        {
            await Task.WhenAll(signals).WaitAsync(TimeSpan.FromMilliseconds(SIGNAL_TIMEOUT_MS));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"Timed out after {SIGNAL_TIMEOUT_MS}ms waiting for {description}.");
        }
        finally
        {
            await monitor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenNoConnectedClients_DoesNotPollSpotify()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(false);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients()).Returns(new HashSet<string>());

        // A fixed wait is correct here: this asserts an absence, so there is
        // no signal to wait for - the delay only has to be long enough that a
        // poll would have happened if the guard were broken.
        using CancellationTokenSource cts = new(100);
        await _monitor.StartAsync(cts.Token);
        await Task.Delay(50);
        await _monitor.StopAsync(CancellationToken.None);

        _mockSpotifyClient.Verify(
            s => s.GetCurrentPlaybackAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenClientsConnectedAndTrackPlays_FetchesLyricsAndBroadcastsToUserGroup()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1" });

        UserSession session = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok-1",
            RefreshToken = "ref-1"
        };

        TrackInfo track = new()
        {
            Id = "track-1",
            Title = "Bohemian Rhapsody",
            Artist = "Queen",
            Duration = TimeSpan.FromMinutes(6)
        };

        PlaybackState playback = new()
        {
            CurrentTrack = track,
            IsPlaying = true,
            Progress = TimeSpan.FromSeconds(10),
            TimestampUtc = DateTimeOffset.UtcNow
        };

        SyncedLyrics lyrics = new()
        {
            TrackId = "track-1",
            Title = "Bohemian Rhapsody",
            Artist = "Queen",
            Lines = [new LyricLine(TimeSpan.FromSeconds(5), "Is this the real life?")]
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playback);

        _mockLyricsProvider.Setup(l => l.GetLyricsAsync(track, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lyrics);

        TaskCompletionSource stateBroadcast = CreateSignal();
        _mockUser1Group
            .Setup(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()))
            .Callback(() => stateBroadcast.TrySetResult())
            .Returns(Task.CompletedTask);

        await RunUntilAsync(_monitor, "a playback-state broadcast to user-1", stateBroadcast.Task);

        _mockLyricsProvider.Verify(l => l.GetLyricsAsync(track, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _mockUser1Group.Verify(c => c.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.AtLeastOnce);
        _mockUser1Group.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.AtLeastOnce);
        _mockAll.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Never);
    }

    [Fact]
    public async Task WhenMultipleUsersConnected_BroadcastsIndividuallyToEachUserGroup()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1", "user-2" });

        UserSession session1 = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok-1",
            RefreshToken = "ref-1"
        };
        UserSession session2 = new()
        {
            Id = "user-2",
            SpotifyUserId = "sp-2",
            DisplayName = "Bob",
            AccessToken = "tok-2",
            RefreshToken = "ref-2"
        };

        TrackInfo track1 = new() { Id = "track-1", Title = "Song 1", Artist = "Artist 1" };
        TrackInfo track2 = new() { Id = "track-2", Title = "Song 2", Artist = "Artist 2" };

        PlaybackState playback1 = new()
        {
            CurrentTrack = track1,
            IsPlaying = true,
            Progress = TimeSpan.FromSeconds(10)
        };
        PlaybackState playback2 = new()
        {
            CurrentTrack = track2,
            IsPlaying = true,
            Progress = TimeSpan.FromSeconds(20)
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session1);
        _mockAuthService.Setup(a => a.GetSessionAsync("user-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session2);

        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playback1);
        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playback2);

        _mockLyricsProvider.Setup(l => l.GetLyricsAsync(track1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncedLyrics
            {
                TrackId = "track-1",
                Title = "Song 1",
                Artist = "Artist 1",
                Lines = []
            });
        _mockLyricsProvider.Setup(l => l.GetLyricsAsync(track2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncedLyrics
            {
                TrackId = "track-2",
                Title = "Song 2",
                Artist = "Artist 2",
                Lines = []
            });

        TaskCompletionSource user1Broadcast = CreateSignal();
        TaskCompletionSource user2Broadcast = CreateSignal();
        _mockUser1Group
            .Setup(c => c.ReceivePlaybackState(
                It.Is<PlaybackStateDto>(p => p.CurrentTrack != null && p.CurrentTrack.Title == "Song 1")))
            .Callback(() => user1Broadcast.TrySetResult())
            .Returns(Task.CompletedTask);
        _mockUser2Group
            .Setup(c => c.ReceivePlaybackState(
                It.Is<PlaybackStateDto>(p => p.CurrentTrack != null && p.CurrentTrack.Title == "Song 2")))
            .Callback(() => user2Broadcast.TrySetResult())
            .Returns(Task.CompletedTask);

        await RunUntilAsync(
            _monitor,
            "both per-user playback-state broadcasts",
            user1Broadcast.Task,
            user2Broadcast.Task);

        _mockUser1Group.Verify(
            c => c.ReceivePlaybackState(
                It.Is<PlaybackStateDto>(p => p.CurrentTrack != null && p.CurrentTrack.Title == "Song 1")),
            Times.AtLeastOnce);
        _mockUser2Group.Verify(
            c => c.ReceivePlaybackState(
                It.Is<PlaybackStateDto>(p => p.CurrentTrack != null && p.CurrentTrack.Title == "Song 2")),
            Times.AtLeastOnce);
        _mockAll.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Never);
    }

    [Fact]
    public async Task WhenTrackChangesToLyriclessTrack_BroadcastsEmptyLyricsToUserGroup()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1" });

        UserSession session = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok-1",
            RefreshToken = "ref-1"
        };

        TrackInfo track = new() { Id = "instrumental-1", Title = "Instrumental Track", Artist = "Composer" };
        PlaybackState playback = new()
        {
            CurrentTrack = track,
            IsPlaying = true,
            Progress = TimeSpan.FromSeconds(5)
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playback);
        _mockLyricsProvider.Setup(l => l.GetLyricsAsync(track, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncedLyrics?)null);

        TaskCompletionSource emptyLyricsBroadcast = CreateSignal();
        _mockUser1Group
            .Setup(c => c.ReceiveLyrics(It.Is<LyricsDto>(l =>
                l.TrackId == "instrumental-1" &&
                l.Title == "Instrumental Track" &&
                l.Lines.Count == 0)))
            .Callback(() => emptyLyricsBroadcast.TrySetResult())
            .Returns(Task.CompletedTask);

        await RunUntilAsync(
            _monitor,
            "the empty-lyrics broadcast for the instrumental track",
            emptyLyricsBroadcast.Task);

        _mockUser1Group.Verify(
            c => c.ReceiveLyrics(It.Is<LyricsDto>(l =>
                l.TrackId == "instrumental-1" &&
                l.Title == "Instrumental Track" &&
                l.Lines.Count == 0)),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task WhenSpotifyReturnsRateLimit_PausesPollingAndBroadcastsRateLimitedDiagnostics()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1" });

        UserSession session = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok-1",
            RefreshToken = "ref-1"
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        Mock<SpotifyAPI.Web.Http.IResponse> mockResponse = new();
        mockResponse.Setup(r => r.Headers).Returns(new Dictionary<string, string>
        {
            { "Retry-After", "120" }
        });

        APITooManyRequestsException rateLimitException = new(mockResponse.Object);

        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(rateLimitException);

        TaskCompletionSource rateLimitedDiagnostics = CreateSignal();
        _mockUser1Group
            .Setup(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
                d.PollerStatus.StartsWith("Rate Limited"))))
            .Callback(() => rateLimitedDiagnostics.TrySetResult())
            .Returns(Task.CompletedTask);

        await RunUntilAsync(_monitor, "the rate-limited diagnostics broadcast", rateLimitedDiagnostics.Task);

        _monitor.IsRateLimited.Should().BeTrue();
        _monitor.RateLimitUntilUtc.Should().BeAfter(DateTimeOffset.UtcNow);

        _mockUser1Group.Verify(
            c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
                d.PollerStatus.StartsWith("Rate Limited"))),
            Times.AtLeastOnce);

        // Should NOT have called GetCurrentPlaybackAsync in a tight loop
        _mockSpotifyClient.Verify(
            s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WhenTrackIsNearEnd_BroadcastsImminentEndInterval()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1" });

        UserSession session = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok-1",
            RefreshToken = "ref-1"
        };

        TrackInfo track = new()
        {
            Id = "track-ending",
            Title = "Ending Soon",
            Artist = "Artist",
            Duration = TimeSpan.FromSeconds(100)
        };

        // Remaining = 2 seconds (<= ImminentEndThresholdMs which is default 5000ms)
        PlaybackState playback = new()
        {
            CurrentTrack = track,
            IsPlaying = true,
            Progress = TimeSpan.FromSeconds(98),
            TimestampUtc = DateTimeOffset.UtcNow
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playback);
        _mockLyricsProvider.Setup(l => l.GetLyricsAsync(track, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncedLyrics?)null);

        IOptions<PlaybackPollerOptions> customOptions = Options.Create(new PlaybackPollerOptions
        {
            ActivePollIntervalMs = 4000,
            ApproachingEndPollIntervalMs = 2500,
            ImminentEndPollIntervalMs = 1234,
            ImminentEndThresholdMs = 5000,
            ApproachingEndThresholdMs = 15000
        });

        ActiveUsersPlaybackMonitor customMonitor = new(
            _mockScopeFactory.Object,
            _mockRegistry.Object,
            _mockHubContext.Object,
            customOptions,
            NullLogger<ActiveUsersPlaybackMonitor>.Instance);

        TaskCompletionSource imminentEndDiagnostics = CreateSignal();
        _mockUser1Group
            .Setup(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
                d.ActivePollIntervalMs == 1234 &&
                d.PollerStatus == "Active (Playing)")))
            .Callback(() => imminentEndDiagnostics.TrySetResult())
            .Returns(Task.CompletedTask);

        await RunUntilAsync(customMonitor, "the imminent-end diagnostics broadcast", imminentEndDiagnostics.Task);

        _mockUser1Group.Verify(
            c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
                d.ActivePollIntervalMs == 1234 &&
                d.PollerStatus == "Active (Playing)")),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task WhenUserTabIsHidden_BroadcastsBackgroundInterval()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1" });
        _mockRegistry.Setup(r => r.IsUserVisible("user-1")).Returns(false);

        UserSession session = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok-1",
            RefreshToken = "ref-1"
        };

        TrackInfo track = new()
        {
            Id = "track-1",
            Title = "Song",
            Artist = "Artist",
            Duration = TimeSpan.FromMinutes(3)
        };

        PlaybackState playback = new()
        {
            CurrentTrack = track,
            IsPlaying = true,
            Progress = TimeSpan.FromSeconds(30),
            TimestampUtc = DateTimeOffset.UtcNow
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playback);
        _mockLyricsProvider.Setup(l => l.GetLyricsAsync(track, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncedLyrics?)null);

        IOptions<PlaybackPollerOptions> customOptions = Options.Create(new PlaybackPollerOptions
        {
            ActivePollIntervalMs = 4000,
            BackgroundPollIntervalMs = 8888
        });

        ActiveUsersPlaybackMonitor customMonitor = new(
            _mockScopeFactory.Object,
            _mockRegistry.Object,
            _mockHubContext.Object,
            customOptions,
            NullLogger<ActiveUsersPlaybackMonitor>.Instance);

        TaskCompletionSource backgroundDiagnostics = CreateSignal();
        _mockUser1Group
            .Setup(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
                d.ActivePollIntervalMs == 8888 &&
                d.PollerStatus == "Active (Background)")))
            .Callback(() => backgroundDiagnostics.TrySetResult())
            .Returns(Task.CompletedTask);

        await RunUntilAsync(customMonitor, "the background-interval diagnostics broadcast", backgroundDiagnostics.Task);

        _mockUser1Group.Verify(
            c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
                d.ActivePollIntervalMs == 8888 &&
                d.PollerStatus == "Active (Background)")),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task WhenSpotifyReturnsUnauthorized_RefreshesTheToken()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1" });

        UserSession session = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "expired-tok",
            RefreshToken = "ref-1"
        };

        UserSession refreshed = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "fresh-tok",
            RefreshToken = "ref-1"
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        Mock<SpotifyAPI.Web.Http.IResponse> mockResponse = new();
        mockResponse.Setup(r => r.Headers).Returns(new Dictionary<string, string>());
        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("expired-tok", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new APIUnauthorizedException(mockResponse.Object));

        TaskCompletionSource refreshCalled = CreateSignal();
        _mockAuthService.Setup(a => a.RefreshTokenAsync("user-1", It.IsAny<CancellationToken>()))
            .Callback(() => refreshCalled.TrySetResult())
            .ReturnsAsync(refreshed);

        await RunUntilAsync(_monitor, "the token refresh triggered by a 401", refreshCalled.Task);

        _mockAuthService.Verify(
            a => a.RefreshTokenAsync("user-1", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task WhenAnUnrelatedErrorMentions401_DoesNotRefreshTheToken()
    {
        // The previous implementation matched on exception text, so any failure
        // whose message merely contained "401" or "Unauthorized" was treated as
        // an expired token and triggered a needless refresh.
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(true);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients())
            .Returns(new HashSet<string> { "user-1" });

        UserSession session = new()
        {
            Id = "user-1",
            SpotifyUserId = "sp-1",
            DisplayName = "Alice",
            AccessToken = "tok-1",
            RefreshToken = "ref-1"
        };

        _mockAuthService.Setup(a => a.GetSessionAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        TaskCompletionSource pollAttempted = CreateSignal();
        _mockSpotifyClient.Setup(s => s.GetCurrentPlaybackAsync("tok-1", It.IsAny<CancellationToken>()))
            .Callback(() => pollAttempted.TrySetResult())
            .ThrowsAsync(new HttpRequestException("Connection reset while reading 401 bytes of payload"));

        TaskCompletionSource refreshCalled = CreateSignal();
        _mockAuthService.Setup(a => a.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => refreshCalled.TrySetResult())
            .ReturnsAsync(session);

        await RunUntilAsync(_monitor, "the failing playback poll", pollAttempted.Task);

        // Bounded negative wait: a refresh would be started right after the
        // catch, so if it has not happened shortly after the poll failed it is
        // not going to. This shape can only ever produce a false pass.
        await Task.WhenAny(refreshCalled.Task, Task.Delay(250));

        refreshCalled.Task.IsCompleted.Should().BeFalse(
            "a transport error that merely mentions 401 is not an expired token");
        _mockAuthService.Verify(
            a => a.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
