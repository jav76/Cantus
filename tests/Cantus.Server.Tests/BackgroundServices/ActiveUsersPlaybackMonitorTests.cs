using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.BackgroundServices;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

        IOptions<PlaybackPollerOptions> options = Options.Create(new PlaybackPollerOptions
        {
            ActivePollIntervalMs = 50,
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

    [Fact]
    public async Task WhenNoConnectedClients_DoesNotPollSpotify()
    {
        _mockRegistry.Setup(r => r.HasConnectedClients).Returns(false);
        _mockRegistry.Setup(r => r.GetActiveUserIdsWithConnectedClients()).Returns(new HashSet<string>());

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

        using CancellationTokenSource cts = new(200);
        await _monitor.StartAsync(cts.Token);
        await Task.Delay(100);
        await _monitor.StopAsync(CancellationToken.None);

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

        using CancellationTokenSource cts = new(200);
        await _monitor.StartAsync(cts.Token);
        await Task.Delay(100);
        await _monitor.StopAsync(CancellationToken.None);

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

        using CancellationTokenSource cts = new(150);
        await _monitor.StartAsync(cts.Token);
        await Task.Delay(80);
        await _monitor.StopAsync(CancellationToken.None);

        _mockUser1Group.Verify(
            c => c.ReceiveLyrics(It.Is<LyricsDto>(l =>
                l.TrackId == "instrumental-1" &&
                l.Title == "Instrumental Track" &&
                l.Lines.Count == 0)),
            Times.AtLeastOnce);
    }
}
