using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Cantus.Core.Models;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class PlaybackTransportTests
{
    private const string TRACK_ID = "t-transport";
    private const long DURATION_MS = 240000;
    private const long LINE_TIMESTAMP_MS = 18000;
    private const long PAST_CONFIRM_TIMEOUT_MS = 5000;

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
        PlaybackTransport.ComputeLineSeekTargetMs(lineTimestampMs, userOffsetMs, durationMs).Should().Be(expectedMs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1500)]
    [InlineData(-2000)]
    public async Task SeekToLine_SendsLineStartAndAppliesOptimisticTimeline(int userOffsetMs)
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: true);
        List<long> applied = new();
        transport.SeekApplied += localNow => applied.Add(localNow);

        // Act
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, userOffsetMs);

        // Assert
        long expectedTargetMs = LINE_TIMESTAMP_MS - userOffsetMs;
        client.Verify(c => c.SeekPlaybackAsync(expectedTargetMs), Times.Once);
        transport.PlaybackState!.ProgressMs.Should().Be(expectedTargetMs);
        transport.PlaybackState.IsPlaying.Should().BeTrue();
        applied.Should().HaveCount(1);
    }

    [Fact]
    public async Task SeekToLine_AppliesOptimisticTimelineBeforeSpotifyAnswers()
    {
        // Arrange - the seek command stays in flight
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        TaskCompletionSource<PlayerCommandResult> pending = new();
        client.Setup(c => c.SeekPlaybackAsync(It.IsAny<long>())).Returns(pending.Task);
        ReceivePoll(transport, progressMs: 12000, isPlaying: true);
        int appliedCount = 0;
        transport.SeekApplied += _ => appliedCount++;

        // Act
        Task seek = transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Assert
        seek.IsCompleted.Should().BeFalse();
        appliedCount.Should().Be(1);
        transport.PlaybackState!.ProgressMs.Should().Be(LINE_TIMESTAMP_MS);

        pending.SetResult(PlayerCommandResult.Success);
        await seek;
        transport.PlaybackState.ProgressMs.Should().Be(LINE_TIMESTAMP_MS);
    }

    [Fact]
    public async Task SeekToLine_WithoutATrack_IsANoOp()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        int appliedCount = 0;
        transport.SeekApplied += _ => appliedCount++;

        // Act
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Assert
        appliedCount.Should().Be(0);
        client.Verify(c => c.SeekPlaybackAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task ApplyServerState_IgnoresStalePollTimelineUntilOneConfirmsTheSeek()
    {
        // Arrange
        (_, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: false);
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Act - an eventually-consistent poll still reports the old position
        PlaybackStatePayload stale = ReceivePoll(transport, progressMs: 12400, isPlaying: false, volumePercent: 40);

        // Assert - the optimistic timeline wins, the rest of the poll is kept
        stale.ProgressMs.Should().Be(LINE_TIMESTAMP_MS);
        transport.PlaybackState!.ProgressMs.Should().Be(LINE_TIMESTAMP_MS);
        transport.VolumePercent.Should().Be(40);

        // Act - the confirming poll
        PlaybackStatePayload confirming = ReceivePoll(transport, progressMs: 18100, isPlaying: false);
        confirming.ProgressMs.Should().Be(18100);

        // Act - after confirmation, playback moving elsewhere is real again
        PlaybackStatePayload moved = ReceivePoll(transport, progressMs: 11000, isPlaying: false);

        // Assert
        moved.ProgressMs.Should().Be(11000);
        transport.HoldAtSeekFloor(11000).Should().Be(11000, "a disagreeing poll after confirmation drops the floor");
    }

    [Fact]
    public async Task ApplyServerState_AfterTheConfirmationWindowLapses_AcceptsTheStalePoll()
    {
        // Arrange
        (_, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: false);
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Act - the same disagreeing poll, arriving after the timeout
        long lateNow = transport.GetServerNowMs() + PAST_CONFIRM_TIMEOUT_MS;
        PlaybackStatePayload late = transport.ApplyServerState(CreateState(12400, isPlaying: false), lateNow);

        // Assert
        late.ProgressMs.Should().Be(12400);
        transport.HoldAtSeekFloor(12400).Should().Be(12400);
    }

    [Fact]
    public async Task ApplyServerState_WhenTrackChangesBeforeConfirmation_AcceptsTheNewTrack()
    {
        // Arrange
        (_, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: true);
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Act - a skip lands before any poll confirmed the seek
        PlaybackStatePayload next = new()
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-next", Title = "Next", Artist = "Artist", DurationMs = DURATION_MS },
            ProgressMs = 5000,
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
        PlaybackStatePayload accepted = transport.ApplyServerState(next, transport.GetServerNowMs());

        // Assert
        accepted.Should().BeSameAs(next);
        transport.PlaybackState.Should().BeSameAs(next);
        transport.HoldAtSeekFloor(5000).Should().Be(5000);
    }

    [Fact]
    public async Task SeekToLine_WhenSpotifyRejectsTheSeek_RevertsAndReportsStatus()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        client.Setup(c => c.SeekPlaybackAsync(It.IsAny<long>())).ReturnsAsync(PlayerCommandResult.NoActiveDevice);
        PlaybackStatePayload serverState = ReceivePoll(transport, progressMs: 12000, isPlaying: true);
        int revertedCount = 0;
        transport.SeekReverted += _ => revertedCount++;

        // Act
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Assert - back on the real timeline, with the shared status-line message
        revertedCount.Should().Be(1);
        transport.PlaybackState.Should().BeSameAs(serverState);
        transport.TransportStatusText.Should().Be("No active Spotify device. Start playback in any Spotify app first.");
        transport.TransportStatusVisibility.Should().Be(Visibility.Visible);
        transport.HoldAtSeekFloor(12000).Should().Be(12000);

        // Act & Assert - no lingering stale-poll guard
        PlaybackStatePayload later = ReceivePoll(transport, progressMs: 15000, isPlaying: true);
        later.ProgressMs.Should().Be(15000);
    }

    [Fact]
    public async Task SeekToLine_WhenASupersededSeekIsRejected_KeepsTheNewerSeek()
    {
        // Arrange - the first seek stays in flight while a second one succeeds
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        TaskCompletionSource<PlayerCommandResult> firstSeek = new();
        client.Setup(c => c.SeekPlaybackAsync(LINE_TIMESTAMP_MS)).Returns(firstSeek.Task);
        ReceivePoll(transport, progressMs: 12000, isPlaying: false);
        int revertedCount = 0;
        transport.SeekReverted += _ => revertedCount++;

        Task first = transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);
        await transport.SeekToLineAsync(40000, 0);

        // Act
        firstSeek.SetResult(PlayerCommandResult.Failed);
        await first;

        // Assert
        revertedCount.Should().Be(0);
        transport.PlaybackState!.ProgressMs.Should().Be(40000);
    }

    [Fact]
    public async Task HoldAtSeekFloor_HoldsTheTappedLineUntilThePositionCatchesUp()
    {
        // Arrange
        (_, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: false);
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Act & Assert - a latency-compensated position just before the line is held
        transport.HoldAtSeekFloor(LINE_TIMESTAMP_MS - 200).Should().Be(LINE_TIMESTAMP_MS);

        // Act & Assert - once reached, the floor is released for good
        transport.HoldAtSeekFloor(LINE_TIMESTAMP_MS + 10).Should().Be(LINE_TIMESTAMP_MS + 10);
        transport.HoldAtSeekFloor(LINE_TIMESTAMP_MS - 200).Should().Be(LINE_TIMESTAMP_MS - 200);
    }

    [Fact]
    public async Task SeekToFraction_ClampsSendsAndDropsTheSeekAnchor()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: false);
        await transport.SeekToLineAsync(LINE_TIMESTAMP_MS, 0);

        // Act
        await transport.SeekToFractionAsync(0.5);
        await transport.SeekToFractionAsync(1.5);

        // Assert
        client.Verify(c => c.SeekPlaybackAsync(DURATION_MS / 2), Times.Once);
        client.Verify(c => c.SeekPlaybackAsync(DURATION_MS), Times.Once);
        transport.HoldAtSeekFloor(1000).Should().Be(1000);
    }

    [Fact]
    public async Task Commands_WithNoTrack_AreNotSent()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();

        // Act
        await transport.TogglePlayPauseAsync();
        await transport.SkipToNextAsync();
        await transport.SkipToPreviousAsync();
        await transport.ToggleShuffleAsync();
        await transport.CycleRepeatAsync();
        await transport.SeekToFractionAsync(0.5);
        transport.RequestVolumeChange(50);
        await transport.SendPendingVolumeAsync();

        // Assert
        client.Verify(c => c.SendPlayerCommandAsync(It.IsAny<string>()), Times.Never);
        client.Verify(c => c.SetShuffleAsync(It.IsAny<bool>()), Times.Never);
        client.Verify(c => c.SetRepeatAsync(It.IsAny<string>()), Times.Never);
        client.Verify(c => c.SeekPlaybackAsync(It.IsAny<long>()), Times.Never);
        client.Verify(c => c.SetPlayerVolumeAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task TogglePlayPause_FlipsPlayingOnlyWhenTheCommandSucceeds()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: true);

        // Act - pause succeeds
        await transport.TogglePlayPauseAsync();

        // Assert
        client.Verify(c => c.SendPlayerCommandAsync("pause"), Times.Once);
        transport.IsPlaying.Should().BeFalse();
        transport.PlayPauseGlyph.Should().Be("▶");

        // Act - resume is rejected
        client.Setup(c => c.SendPlayerCommandAsync("resume")).ReturnsAsync(PlayerCommandResult.MissingPermissions);
        await transport.TogglePlayPauseAsync();

        // Assert
        transport.IsPlaying.Should().BeFalse();
        transport.TransportStatusText.Should().Contain("reconnect");
    }

    [Fact]
    public async Task ShuffleAndRepeat_UpdateOnSuccessAndCycleLikeSpotify()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: true);

        // Act
        await transport.ToggleShuffleAsync();
        List<string> modes = new();
        for (int i = 0; i < 3; i++)
        {
            await transport.CycleRepeatAsync();
            modes.Add(transport.RepeatMode);
        }

        // Assert
        client.Verify(c => c.SetShuffleAsync(true), Times.Once);
        transport.IsShuffled.Should().BeTrue();
        modes.Should().Equal("context", "track", "off");
    }

    [Fact]
    public async Task SendPendingVolume_SendsTheClampedRequestedValue()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTransport transport) = CreateTransport();
        ReceivePoll(transport, progressMs: 12000, isPlaying: true, volumePercent: 30);

        // Act - only the value the slider rests on is sent
        transport.RequestVolumeChange(60);
        transport.RequestVolumeChange(150);
        await transport.SendPendingVolumeAsync();

        // Assert
        client.Verify(c => c.SetPlayerVolumeAsync(100), Times.Once);
        client.Verify(c => c.SetPlayerVolumeAsync(60), Times.Never);
        transport.VolumePercent.Should().Be(100);
        transport.VolumeText.Should().Be("100%");
        transport.VolumeSliderValue.Should().Be(100);
    }

    [Fact]
    public void ApplyServerState_CopiesTransportFieldsAndResetsThemWithoutATrack()
    {
        // Arrange
        (_, PlaybackTransport transport) = CreateTransport();
        PlaybackStatePayload playing = CreateState(12000, isPlaying: true, volumePercent: 70);
        playing.IsShuffled = true;
        playing.RepeatMode = "track";

        // Act
        transport.ApplyServerState(playing, transport.GetServerNowMs());

        // Assert
        transport.IsPlaying.Should().BeTrue();
        transport.VolumePercent.Should().Be(70);
        transport.IsShuffled.Should().BeTrue();
        transport.RepeatMode.Should().Be("track");

        // Act - nothing playing any more
        transport.ApplyServerState(new PlaybackStatePayload { IsPlaying = true }, transport.GetServerNowMs());

        // Assert
        transport.IsPlaying.Should().BeFalse();
        transport.VolumePercent.Should().BeNull();
        transport.VolumeText.Should().Be("\u2014");
        transport.IsShuffled.Should().BeFalse();
        transport.RepeatMode.Should().Be("off");
    }

    [Fact]
    public void ReportTransportResult_MapsResultsToUserGuidance()
    {
        // Arrange
        (_, PlaybackTransport transport) = CreateTransport();
        List<string?> changed = new();
        transport.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act & Assert
        transport.ReportTransportResult(PlayerCommandResult.Failed);
        transport.TransportStatusText.Should().Be("Couldn't reach Spotify. Try again.");
        changed.Should().Contain(nameof(PlaybackTransport.TransportStatusVisibility));

        transport.ReportTransportResult(PlayerCommandResult.Success);
        transport.TransportStatusText.Should().BeEmpty();
        transport.TransportStatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void DerivedProperties_NotifyWithTheirSources()
    {
        // Arrange
        ThemeManager theme = new();
        PlaybackTransport transport = new(new Mock<ISignalRPlaybackClient>().Object, theme);
        List<string?> changed = new();
        transport.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        transport.IsPlaying = true;
        transport.IsShuffled = true;
        transport.RepeatMode = "track";
        transport.VolumePercent = 20;

        // Assert
        changed.Should().Contain(new[]
        {
            nameof(PlaybackTransport.PlayPauseGlyph),
            nameof(PlaybackTransport.ShuffleButtonForeground),
            nameof(PlaybackTransport.RepeatGlyph),
            nameof(PlaybackTransport.RepeatButtonForeground),
            nameof(PlaybackTransport.VolumeSliderValue),
            nameof(PlaybackTransport.VolumeText),
        });
        transport.ShuffleButtonForeground.Should().BeSameAs(theme.PrimaryAccentBrush);
        transport.RepeatButtonForeground.Should().BeSameAs(theme.PrimaryAccentBrush);
        transport.RepeatGlyph.Should().Be("\uE8ED");
    }

    [Theory]
    [InlineData(false, "off", "\u25B6", "\uE8EE")]
    [InlineData(true, "context", "\u23F8", "\uE8EE")]
    [InlineData(true, "track", "\u23F8", "\uE8ED")]
    public void Glyphs_MapStateToTheirCodePoints(
        bool isPlaying,
        string repeatMode,
        string expectedPlayPause,
        string expectedRepeat)
    {
        // Arrange
        (_, PlaybackTransport transport) = CreateTransport();

        // Act
        transport.IsPlaying = isPlaying;
        transport.RepeatMode = repeatMode;

        // Assert
        transport.PlayPauseGlyph.Should().Be(expectedPlayPause);
        transport.RepeatGlyph.Should().Be(expectedRepeat);
    }

    private static (Mock<ISignalRPlaybackClient> Client, PlaybackTransport Transport) CreateTransport()
    {
        Mock<ISignalRPlaybackClient> client = new();
        client.Setup(c => c.SeekPlaybackAsync(It.IsAny<long>())).ReturnsAsync(PlayerCommandResult.Success);
        client.Setup(c => c.SendPlayerCommandAsync(It.IsAny<string>())).ReturnsAsync(PlayerCommandResult.Success);
        client.Setup(c => c.SetShuffleAsync(It.IsAny<bool>())).ReturnsAsync(PlayerCommandResult.Success);
        client.Setup(c => c.SetRepeatAsync(It.IsAny<string>())).ReturnsAsync(PlayerCommandResult.Success);
        client.Setup(c => c.SetPlayerVolumeAsync(It.IsAny<int>())).ReturnsAsync(PlayerCommandResult.Success);
        PlaybackTransport transport = new(client.Object, new ThemeManager());
        return (client, transport);
    }

    private static PlaybackStatePayload ReceivePoll(
        PlaybackTransport transport,
        long progressMs,
        bool isPlaying,
        int? volumePercent = null)
    {
        return transport.ApplyServerState(
            CreateState(progressMs, isPlaying, volumePercent),
            transport.GetServerNowMs());
    }

    private static PlaybackStatePayload CreateState(long progressMs, bool isPlaying, int? volumePercent = null)
    {
        return new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = TRACK_ID, Title = "Song", Artist = "Artist", DurationMs = DURATION_MS },
            ProgressMs = progressMs,
            IsPlaying = isPlaying,
            VolumePercent = volumePercent,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }
}
