using System;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class OptimisticSeekAnchorTests
{
    private const string TRACK_ID = "t-anchor";
    private const long DURATION_MS = 240000;
    private const long ISSUED_AT_MS = 1_000_000;
    private const long LINE_TIMESTAMP_MS = 18000;
    private const long POLL_PROGRESS_MS = 12000;
    private const long ELAPSED_MS = 750;
    private const int VOLUME_PERCENT = 40;

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
        OptimisticSeekAnchor.ComputeLineSeekTargetMs(lineTimestampMs, userOffsetMs, durationMs).Should().Be(expectedMs);
    }

    [Fact]
    public void Reconcile_WithoutAnAnchor_PassesThePollThrough()
    {
        // Arrange
        OptimisticSeekAnchor anchor = new();
        PlaybackStatePayload poll = CreateState(POLL_PROGRESS_MS, isPlaying: true);

        // Act
        PlaybackStatePayload result = anchor.Reconcile(poll, expected: null, ISSUED_AT_MS);

        // Assert
        result.Should().BeSameAs(poll);
        anchor.Revert().Should().BeSameAs(poll, "the last poll is what a rejected seek restores");
    }

    [Fact]
    public void Begin_CopiesTheStateAtTheTargetAndKeepsEverythingElse()
    {
        // Arrange
        OptimisticSeekAnchor anchor = new();
        PlaybackStatePayload state = CreateState(POLL_PROGRESS_MS, isPlaying: true);
        state.DeviceName = "Kitchen";
        state.VolumePercent = VOLUME_PERCENT;

        // Act
        PlaybackStatePayload optimistic = anchor.Begin(
            state,
            LINE_TIMESTAMP_MS,
            isPlaying: true,
            ISSUED_AT_MS,
            LINE_TIMESTAMP_MS);

        // Assert
        optimistic.Should().NotBeSameAs(state);
        optimistic.ProgressMs.Should().Be(LINE_TIMESTAMP_MS);
        optimistic.TimestampUtc.ToUnixTimeMilliseconds().Should().Be(ISSUED_AT_MS);
        optimistic.CurrentTrack.Should().BeSameAs(state.CurrentTrack);
        optimistic.DeviceName.Should().Be("Kitchen");
        optimistic.VolumePercent.Should().Be(VOLUME_PERCENT);
        anchor.HoldAtFloor(LINE_TIMESTAMP_MS - 1).Should().Be(LINE_TIMESTAMP_MS);
    }

    [Fact]
    public void Begin_KeepsTheLocalSourceAndCapabilities()
    {
        // Arrange
        OptimisticSeekAnchor anchor = new();
        PlaybackStatePayload state = CreateState(POLL_PROGRESS_MS, isPlaying: true);
        state.Source = PlaybackStatePayload.SOURCE_LOCAL;
        state.Capabilities = new PlayerCapabilitiesPayload { CanPlay = true, CanPause = true, CanSeek = true };

        // Act
        PlaybackStatePayload optimistic = anchor.Begin(state, LINE_TIMESTAMP_MS, isPlaying: true, ISSUED_AT_MS, LINE_TIMESTAMP_MS);

        // Assert
        optimistic.Source.Should().Be(PlaybackStatePayload.SOURCE_LOCAL);
        optimistic.Capabilities.Should().BeSameAs(state.Capabilities);
    }

    [Fact]
    public void Revert_WithNoTrackToRestore_ReturnsNullAndDropsTheAnchor()
    {
        // Arrange
        OptimisticSeekAnchor anchor = new();
        anchor.Reconcile(new PlaybackStatePayload(), expected: null, ISSUED_AT_MS);
        anchor.Begin(
            CreateState(POLL_PROGRESS_MS, isPlaying: false),
            LINE_TIMESTAMP_MS,
            isPlaying: false,
            ISSUED_AT_MS,
            LINE_TIMESTAMP_MS);

        // Act
        PlaybackStatePayload? restored = anchor.Revert();

        // Assert
        restored.Should().BeNull();
        anchor.HoldAtFloor(POLL_PROGRESS_MS).Should().Be(POLL_PROGRESS_MS);
    }

    [Fact]
    public void NextGeneration_OnlyTheLatestIsCurrent()
    {
        // Arrange
        OptimisticSeekAnchor anchor = new();

        // Act
        int first = anchor.NextGeneration();
        int second = anchor.NextGeneration();

        // Assert
        anchor.IsCurrentGeneration(first).Should().BeFalse();
        anchor.IsCurrentGeneration(second).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, POLL_PROGRESS_MS + ELAPSED_MS)]
    [InlineData(false, POLL_PROGRESS_MS)]
    public void ProjectProgressMs_ExtrapolatesOnlyWhilePlaying(bool isPlaying, long expectedMs)
    {
        // Arrange
        PlaybackStatePayload state = CreateState(POLL_PROGRESS_MS, isPlaying);
        state.TimestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(ISSUED_AT_MS);

        // Act & Assert
        OptimisticSeekAnchor.ProjectProgressMs(state, ISSUED_AT_MS + ELAPSED_MS).Should().Be(expectedMs);
    }

    private static PlaybackStatePayload CreateState(long progressMs, bool isPlaying)
    {
        return new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = TRACK_ID, Title = "Song", Artist = "Artist", DurationMs = DURATION_MS },
            ProgressMs = progressMs,
            IsPlaying = isPlaying,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }
}
