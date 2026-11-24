using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class PlaybackUpdateApplierTests
{
    private const string SESSION_ID = "local-session";
    private const string DISPLAY_NAME = "Desktop";
    private const string TRACK_ID = "local:0123456789abcdef0123456789abcdef";
    private const string OTHER_TRACK_ID = "local:fedcba9876543210fedcba9876543210";
    private const int SAVED_OFFSET_MS = 150;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly PlaybackSessionRegistry _registry = new();
    private readonly Mock<IPlaybackClient> _owners = new();
    private readonly Mock<IPlaybackClient> _displays = new();
    private readonly Mock<ILyricsProvider> _lyricsProvider = new();
    private readonly Mock<ILyricsCacheRepository> _lyricsCache = new();
    private readonly PlaybackUpdateApplier _applier;

    public PlaybackUpdateApplierTests()
    {
        Mock<IHubClients<IPlaybackClient>> clients = new();
        clients.Setup(c => c.Group(PlaybackGroups.Owner(SESSION_ID))).Returns(_owners.Object);
        clients.Setup(c => c.Group(PlaybackGroups.Displays(SESSION_ID))).Returns(_displays.Object);
        Mock<IHubContext<PlaybackHub, IPlaybackClient>> hubContext = new();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        _lyricsCache.Setup(c => c.GetTrackOffsetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SAVED_OFFSET_MS);
        _lyricsProvider.Setup(p => p.GetLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackInfo track, CancellationToken _) => LyricsFor(track.Id, "Line"));

        _applier = new PlaybackUpdateApplier(
            _registry,
            hubContext.Object,
            _time,
            NullLogger<PlaybackUpdateApplier>.Instance);
    }

    [Fact]
    public async Task ApplyAsync_OnFirstTrack_FetchesLyricsAndOffsetAndBroadcastsToOwnerAndDisplays()
    {
        await ApplyAsync(State(TRACK_ID, isPlaying: true));

        UserPlaybackSnapshot snapshot = _registry.GetUserState(SESSION_ID)!;
        snapshot.PlaybackState!.CurrentTrack!.Id.Should().Be(TRACK_ID);
        snapshot.Lyrics!.TrackId.Should().Be(TRACK_ID);
        snapshot.TrackOffsetMs.Should().Be(SAVED_OFFSET_MS);

        _owners.Verify(c => c.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == TRACK_ID && l.Lines.Count == 1)), Times.Once);
        _owners.Verify(c => c.ReceiveTrackOffset(It.Is<TrackOffsetDto>(o =>
            o.TrackId == TRACK_ID && o.OffsetMs == SAVED_OFFSET_MS)), Times.Once);
        _owners.Verify(c => c.ReceivePlaybackState(It.Is<PlaybackStateDto>(s =>
            s.Source == PlaybackStateDto.SOURCE_LOCAL
            && s.ActiveUserId == SESSION_ID
            && s.ActiveUserDisplayName == DISPLAY_NAME
            && s.Capabilities != null && s.Capabilities.CanSeek)), Times.Once);
        _owners.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
            d.PollerStatus == PlaybackUpdateApplier.STATUS_LOCAL_PLAYING && d.ActiveUserId == SESSION_ID)), Times.Once);

        _displays.Verify(c => c.ReceivePlaybackState(It.Is<PlaybackStateDto>(s =>
            s.ActiveUserId == null && s.ActiveUserDisplayName == DISPLAY_NAME && s.Source == PlaybackStateDto.SOURCE_LOCAL)), Times.Once);
        _displays.Verify(c => c.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == TRACK_ID)), Times.Once);
        _displays.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d => d.ActiveUserId == null)), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_OnSameTrack_KeepsTheSnapshotLyricsAndDoesNotRefetch()
    {
        await ApplyAsync(State(TRACK_ID, isPlaying: true));
        SyncedLyrics overridden = LyricsFor(TRACK_ID, "Override");
        _registry.ReplaceLyricsForTrack(TRACK_ID, overridden);

        await ApplyAsync(State(TRACK_ID, isPlaying: false));

        _lyricsProvider.Verify(p => p.GetLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.GetUserState(SESSION_ID)!.Lyrics.Should().BeSameAs(overridden);
        _registry.GetUserState(SESSION_ID)!.PlaybackState!.IsPlaying.Should().BeFalse();
        _owners.Verify(c => c.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Once);
        _owners.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
            d.PollerStatus == PlaybackUpdateApplier.STATUS_LOCAL_PAUSED)), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_OnTrackChangeWhilePaused_StillFetchesLyrics()
    {
        await ApplyAsync(State(TRACK_ID, isPlaying: true));

        await ApplyAsync(State(OTHER_TRACK_ID, isPlaying: false));

        _lyricsProvider.Verify(
            p => p.GetLyricsAsync(It.Is<TrackInfo>(t => t.Id == OTHER_TRACK_ID), It.IsAny<CancellationToken>()),
            Times.Once);
        _owners.Verify(c => c.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == OTHER_TRACK_ID)), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_OverLyricBudget_BroadcastsEmptyLyricsWithoutCallingTheProvider()
    {
        await _applier.ApplyAsync(
            new PlaybackUpdate(SESSION_ID, DISPLAY_NAME, State(TRACK_ID, isPlaying: true), AllowLyricsFetch: false),
            _lyricsProvider.Object,
            _lyricsCache.Object);

        _lyricsProvider.Verify(p => p.GetLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        _owners.Verify(c => c.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == TRACK_ID && l.Lines.Count == 0)), Times.Once);
        _owners.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WhenLyricsLookupFails_StillBroadcastsTheTrackWithEmptyLyrics()
    {
        _lyricsProvider.Setup(p => p.GetLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider down"));

        await ApplyAsync(State(TRACK_ID, isPlaying: true));

        _owners.Verify(c => c.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == TRACK_ID && l.Lines.Count == 0)), Times.Once);
        _owners.Verify(c => c.ReceivePlaybackState(It.IsAny<PlaybackStateDto>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WithNoTrack_ClearsTheSnapshotAndBroadcastsAnEmptyState()
    {
        await ApplyAsync(State(TRACK_ID, isPlaying: true));

        await ApplyAsync(new PlaybackState { Source = PlaybackSource.Local, TimestampUtc = _time.GetUtcNow() });

        UserPlaybackSnapshot snapshot = _registry.GetUserState(SESSION_ID)!;
        snapshot.PlaybackState.Should().BeNull();
        snapshot.Lyrics.Should().BeNull();
        _owners.Verify(c => c.ReceivePlaybackState(It.Is<PlaybackStateDto>(s =>
            s.CurrentTrack == null && !s.IsPlaying && s.Source == PlaybackStateDto.SOURCE_LOCAL)), Times.Once);
        _owners.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
            d.PollerStatus == PlaybackUpdateApplier.STATUS_LOCAL_IDLE)), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_AfterNoTrack_TreatsTheSameTrackAsAChange()
    {
        await ApplyAsync(State(TRACK_ID, isPlaying: true));
        await ApplyAsync(new PlaybackState { Source = PlaybackSource.Local });

        await ApplyAsync(State(TRACK_ID, isPlaying: true));

        _lyricsProvider.Verify(p => p.GetLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _registry.GetUserState(SESSION_ID)!.Lyrics.Should().NotBeNull();
    }

    [Fact]
    public async Task Forget_MakesTheNextUpdateATrackChange()
    {
        await ApplyAsync(State(TRACK_ID, isPlaying: true));

        _applier.Forget(SESSION_ID);
        await ApplyAsync(State(TRACK_ID, isPlaying: true));

        _lyricsProvider.Verify(p => p.GetLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task MarkPublisherOfflineAsync_PausesAtTheProjectedPositionAndKeepsTrackAndLyrics()
    {
        PlaybackState playing = State(TRACK_ID, isPlaying: true) with
        {
            Progress = TimeSpan.FromSeconds(30),
            TimestampUtc = _time.GetUtcNow()
        };
        await ApplyAsync(playing);
        _time.Advance(TimeSpan.FromSeconds(20));

        await _applier.MarkPublisherOfflineAsync(SESSION_ID, DISPLAY_NAME);

        UserPlaybackSnapshot snapshot = _registry.GetUserState(SESSION_ID)!;
        snapshot.PlaybackState!.IsPlaying.Should().BeFalse();
        snapshot.PlaybackState.Progress.Should().Be(TimeSpan.FromSeconds(50));
        snapshot.PlaybackState.CurrentTrack!.Id.Should().Be(TRACK_ID);
        snapshot.Lyrics!.TrackId.Should().Be(TRACK_ID);
        _owners.Verify(c => c.ReceivePlaybackState(It.Is<PlaybackStateDto>(s =>
            !s.IsPlaying && s.ProgressMs == 50_000 && s.CurrentTrack!.Id == TRACK_ID)), Times.Once);
        _displays.Verify(c => c.ReceivePlaybackState(It.Is<PlaybackStateDto>(s =>
            !s.IsPlaying && s.ActiveUserId == null)), Times.Once);
        _owners.Verify(c => c.ReceiveDiagnostics(It.Is<DiagnosticsDto>(d =>
            d.PollerStatus == PlaybackUpdateApplier.STATUS_PUBLISHER_OFFLINE)), Times.Once);
    }

    [Fact]
    public async Task MarkPublisherOfflineAsync_DoesNotProjectPastTheEndOfTheTrack()
    {
        await ApplyAsync(State(TRACK_ID, isPlaying: true) with
        {
            Progress = TimeSpan.FromSeconds(170),
            TimestampUtc = _time.GetUtcNow()
        });
        _time.Advance(TimeSpan.FromMinutes(5));

        await _applier.MarkPublisherOfflineAsync(SESSION_ID, DISPLAY_NAME);

        _registry.GetUserState(SESSION_ID)!.PlaybackState!.Progress.Should().Be(TimeSpan.FromMinutes(3));
    }

    private Task ApplyAsync(PlaybackState state) =>
        _applier.ApplyAsync(
            new PlaybackUpdate(SESSION_ID, DISPLAY_NAME, state),
            _lyricsProvider.Object,
            _lyricsCache.Object);

    private PlaybackState State(string trackId, bool isPlaying)
    {
        return new PlaybackState
        {
            CurrentTrack = new TrackInfo
            {
                Id = trackId,
                Title = "Song " + trackId,
                Artist = "Artist",
                Duration = TimeSpan.FromMinutes(3)
            },
            IsPlaying = isPlaying,
            Progress = TimeSpan.FromSeconds(10),
            TimestampUtc = _time.GetUtcNow(),
            Source = PlaybackSource.Local,
            Capabilities = new PlaybackCapabilities { CanPlay = true, CanPause = true, CanSeek = true }
        };
    }

    private static SyncedLyrics LyricsFor(string trackId, string text)
    {
        return new SyncedLyrics
        {
            TrackId = trackId,
            Title = "Song",
            Artist = "Artist",
            Lines = [new LyricLine(TimeSpan.Zero, text)]
        };
    }
}
