using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Cantus.Core.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelSeekToLineTests
{
    private const string TRACK_ID = "t-seek";
    private const long DURATION_MS = 240000;

    // Line 3 is a "♪" placeholder followed by an 18s gap (an instrumental break).
    private static readonly long[] LINE_TIMESTAMPS_MS = { 10000, 14000, 18000, 22000, 40000 };

    [Theory]
    [InlineData(30000, 0, DURATION_MS, 30000)]
    [InlineData(30000, 1500, DURATION_MS, 28500)]
    [InlineData(30000, -2000, DURATION_MS, 32000)]
    [InlineData(1000, 1500, DURATION_MS, 0)]
    [InlineData(239500, -2000, DURATION_MS, DURATION_MS)]
    [InlineData(30000, -2000, 0, 32000)]
    public void ComputeLineSeekTargetMs_UndoesUserOffsetAndClampsToTrack(
        long lineTimestampMs,
        int userOffsetMs,
        long durationMs,
        long expectedMs)
    {
        LyricsViewModel.ComputeLineSeekTargetMs(lineTimestampMs, userOffsetMs, durationMs).Should().Be(expectedMs);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(200, 0)]
    [InlineData(1000, 0)]
    [InlineData(200, 1500)]
    [InlineData(1000, -2000)]
    public async Task SeekToLine_SendsLineStartAndActivatesTappedLine_NotThePreviousOne(
        int latencyMs,
        int userOffsetMs)
    {
        // Arrange - playing on line 0
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(latencyMs);
        LoadSyncedLyrics(client);
        RaiseTrackOffset(client, userOffsetMs);
        RaisePlayback(client, progressMs: 12000 - userOffsetMs, isPlaying: true);
        vm.AdvancePlayback();
        vm.ActiveLineIndex.Should().Be(0);

        // Act
        await vm.SeekToLineAsync(2);
        vm.AdvancePlayback();

        // Assert - the latency-free line start goes to Spotify, and the
        // latency-compensated display still lands on the tapped line
        client.Verify(c => c.SeekPlaybackAsync(18000 - userOffsetMs), Times.Once);
        vm.ActiveLineIndex.Should().Be(2);
        vm.LyricLines[2].IsActive.Should().BeTrue();
        vm.LyricLines[1].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SeekToLine_HighlightsTappedLineBeforeSpotifyAnswers()
    {
        // Arrange - the seek command stays in flight
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        TaskCompletionSource<PlayerCommandResult> pending = new();
        client.Setup(c => c.SeekPlaybackAsync(It.IsAny<long>())).Returns(pending.Task);
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: true);
        vm.AdvancePlayback();

        List<int> scrolledTo = new();
        vm.ActiveLineChanged += idx => scrolledTo.Add(idx);

        // Act
        Task seek = vm.SeekToLineAsync(2);

        // Assert - optimistic: active and scrolled to before the round trip
        seek.IsCompleted.Should().BeFalse();
        vm.ActiveLineIndex.Should().Be(2);
        scrolledTo.Should().Equal(2);

        pending.SetResult(PlayerCommandResult.Success);
        await seek;
        vm.ActiveLineIndex.Should().Be(2);
    }

    [Fact]
    public async Task SeekToLine_IgnoresStalePollsUntilOneConfirmsTheSeek()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: true);
        vm.AdvancePlayback();
        await vm.SeekToLineAsync(2);

        // Act - an eventually-consistent poll still reports the old position
        RaisePlayback(client, progressMs: 12400, isPlaying: true);
        vm.AdvancePlayback();

        // Assert - no snap back to line 0
        vm.ActiveLineIndex.Should().Be(2);

        // Act - the confirming poll
        RaisePlayback(client, progressMs: 18100, isPlaying: true);
        vm.AdvancePlayback();
        vm.ActiveLineIndex.Should().Be(2);

        // Act - after confirmation, playback moving elsewhere is real again
        // (e.g. a seek from the Spotify app)
        RaisePlayback(client, progressMs: 11000, isPlaying: true);
        vm.AdvancePlayback();

        // Assert
        vm.ActiveLineIndex.Should().Be(0);
    }

    [Fact]
    public async Task SeekToLine_WhilePaused_HoldsTappedLineDespiteLatencyCompensation()
    {
        // Arrange - paused on line 0; paused positions still subtract latency
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: false);
        vm.AdvancePlayback();

        // Act - tap, then the confirming paused poll at exactly the line start
        await vm.SeekToLineAsync(1);
        RaisePlayback(client, progressMs: 14000, isPlaying: false);
        vm.AdvancePlayback();
        vm.AdvancePlayback();

        // Assert - 14000 - 200ms would be line 0; the display floor keeps line 1
        client.Verify(c => c.SeekPlaybackAsync(14000), Times.Once);
        vm.ActiveLineIndex.Should().Be(1);
        vm.ProgressText.Should().Be("00:14");
    }

    [Fact]
    public async Task SeekToLine_WhenSpotifyRejectsTheSeek_RevertsAndReportsStatus()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        client.Setup(c => c.SeekPlaybackAsync(It.IsAny<long>())).ReturnsAsync(PlayerCommandResult.NoActiveDevice);
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: true);
        vm.AdvancePlayback();

        // Act
        await vm.SeekToLineAsync(2);
        vm.AdvancePlayback();

        // Assert - back on the real line, with the shared status-line message
        vm.ActiveLineIndex.Should().Be(0);
        vm.TransportStatusText.Should().Be("No active Spotify device. Start playback in any Spotify app first.");

        // Act & Assert - no lingering stale-poll guard
        RaisePlayback(client, progressMs: 15000, isPlaying: true);
        vm.AdvancePlayback();
        vm.ActiveLineIndex.Should().Be(1);
    }

    [Fact]
    public async Task SeekToLine_WithPlainLyricsOnly_IsANoOp()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        client.Raise(c => c.LyricsReceived += null, new LyricsPayload
        {
            TrackId = TRACK_ID,
            Title = "Song",
            Artist = "Artist",
            PlainLyrics = "first line\nsecond line",
            Lines = new List<LyricLinePayload>()
        });
        RaisePlayback(client, progressMs: 12000, isPlaying: true);

        // Act
        await vm.SeekToLineAsync(0);

        // Assert
        vm.CanSeekToLines.Should().BeFalse();
        client.Verify(c => c.SeekPlaybackAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task SeekToLine_InStaticViewOfSyncedLyrics_IsANoOp()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: true);
        vm.AdvancePlayback();
        vm.ToggleStaticLyricsMode();

        // Act
        await vm.SeekToLineAsync(2);
        vm.AdvancePlayback();

        // Assert
        vm.CanSeekToLines.Should().BeFalse();
        vm.ActiveLineIndex.Should().Be(0);
        client.Verify(c => c.SeekPlaybackAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task SeekToLine_WithoutTrackOrWithBadIndex_IsANoOp()
    {
        // Arrange - lyrics but no playback state yet
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        LoadSyncedLyrics(client);

        // Act & Assert
        vm.CanSeekToLines.Should().BeFalse();
        await vm.SeekToLineAsync(1);

        RaisePlayback(client, progressMs: 12000, isPlaying: true);
        vm.CanSeekToLines.Should().BeTrue();
        await vm.SeekToLineAsync(-1);
        await vm.SeekToLineAsync(LINE_TIMESTAMPS_MS.Length);

        client.Verify(c => c.SeekPlaybackAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task SeekToLine_OnTheActiveLine_RestartsItsUnderlineAndRecenters()
    {
        // Arrange - halfway through line 0
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(0);
        vm.IsKaraokeModeEnabled = true;
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: false);
        vm.AdvancePlayback();
        vm.LyricLines[0].LineProgressFraction.Should().BeApproximately(0.5, 0.01);

        List<int> scrolledTo = new();
        vm.ActiveLineChanged += idx => scrolledTo.Add(idx);

        // Act
        await vm.SeekToLineAsync(0);

        // Assert
        client.Verify(c => c.SeekPlaybackAsync(10000), Times.Once);
        vm.ActiveLineIndex.Should().Be(0);
        vm.LyricLines[0].LineProgressFraction.Should().Be(0.0);
        scrolledTo.Should().Equal(0);
    }

    [Fact]
    public async Task SeekToLine_IntoAndOutOfAnInstrumentalBreak_UpdatesTheCountdown()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        vm.IsKaraokeModeEnabled = true;
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: false);
        vm.AdvancePlayback();
        vm.IsInstrumentalBreak.Should().BeFalse();

        // Act - tap the "♪" placeholder that opens an 18s interlude
        await vm.SeekToLineAsync(3);

        // Assert - the countdown starts from the full gap
        vm.LyricLines[3].Text.Should().Be("♪");
        vm.ActiveLineIndex.Should().Be(3);
        vm.IsInstrumentalBreak.Should().BeTrue();
        vm.InstrumentalBreakText.Should().Be("♪ Instrumental Interlude (18s) ♪");

        // Act - jump back out to a sung line
        await vm.SeekToLineAsync(1);

        // Assert
        vm.ActiveLineIndex.Should().Be(1);
        vm.IsInstrumentalBreak.Should().BeFalse();
        vm.LyricLines[1].LineProgressFraction.Should().Be(0.0);
        vm.LyricLines[3].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SeekToLine_EndsManualScrollPause()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        vm.IsAutoScrollEnabled = true;
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: true);
        vm.SetUserScrollingPaused(true);

        // Act
        await vm.SeekToLineAsync(2);

        // Assert
        vm.IsUserScrollingPaused.Should().BeFalse();
    }

    [Fact]
    public async Task SeekToLine_WhenTrackChangesBeforeConfirmation_AcceptsTheNewTrack()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm) = CreateViewModel(200);
        LoadSyncedLyrics(client);
        RaisePlayback(client, progressMs: 12000, isPlaying: true);
        await vm.SeekToLineAsync(2);

        // Act - a skip lands before any poll confirmed the seek
        client.Raise(c => c.PlaybackStateReceived += null, new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-next", Title = "Next", Artist = "Artist", DurationMs = DURATION_MS },
            ProgressMs = 5000,
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow
        });
        vm.AdvancePlayback();

        // Assert - the new track's position is taken as-is
        vm.CurrentTitle.Should().Be("Next");
        vm.ActiveLineIndex.Should().Be(-1);
    }

    private static (Mock<ISignalRPlaybackClient> Client, LyricsViewModel Vm) CreateViewModel(int latencyMs)
    {
        Mock<ISignalRPlaybackClient> client = new();
        client.Setup(c => c.SeekPlaybackAsync(It.IsAny<long>())).ReturnsAsync(PlayerCommandResult.Success);
        LyricsViewModel vm = new(client.Object, new ThemeManager(), new ResponsiveLayoutManager());
        vm.LatencyCompensationMs = latencyMs;
        return (client, vm);
    }

    private static void LoadSyncedLyrics(Mock<ISignalRPlaybackClient> client)
    {
        List<LyricLinePayload> lines = new();
        for (int i = 0; i < LINE_TIMESTAMPS_MS.Length; i++)
        {
            string text = i == 3 ? string.Empty : $"line {i}";
            lines.Add(new LyricLinePayload { TimestampMs = LINE_TIMESTAMPS_MS[i], Text = text });
        }

        client.Raise(c => c.LyricsReceived += null, new LyricsPayload
        {
            TrackId = TRACK_ID,
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = lines
        });
    }

    private static void RaisePlayback(Mock<ISignalRPlaybackClient> client, long progressMs, bool isPlaying)
    {
        client.Raise(c => c.PlaybackStateReceived += null, new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = TRACK_ID, Title = "Song", Artist = "Artist", DurationMs = DURATION_MS },
            ProgressMs = progressMs,
            IsPlaying = isPlaying,
            TimestampUtc = DateTimeOffset.UtcNow
        });
    }

    private static void RaiseTrackOffset(Mock<ISignalRPlaybackClient> client, int offsetMs)
    {
        RaisePlayback(client, progressMs: 0, isPlaying: false);
        client.Raise(c => c.TrackOffsetReceived += null, new TrackOffsetPayload { TrackId = TRACK_ID, OffsetMs = offsetMs });
    }
}
