using System.Collections.Generic;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class PlaybackTimingTests
{
    private const string TRACK_ID = "t-timing";
    private const string OTHER_TRACK_ID = "t-other";
    private const long DURATION_MS = 240000;
    private const int DEFAULT_LATENCY_MS = 200;
    private const int OFFSET_NUDGE_MS = 500;

    [Theory]
    [InlineData(60000, 200, 0, 59800)]
    [InlineData(60000, 0, 0, 60000)]
    [InlineData(60000, 200, 1500, 61300)]
    [InlineData(60000, 1000, -2000, 57000)]
    public void RenderPosition_IsPlaybackMinusLatencyPlusTrackOffset(
        long projectedProgressMs,
        int latencyMs,
        int trackOffsetMs,
        long expectedRenderMs)
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(latencyMs);
        timing.ApplyTrackOffset(new TrackOffsetPayload { TrackId = TRACK_ID, OffsetMs = trackOffsetMs });

        // Act
        long clockTargetMs = timing.ComputeClockTargetMs(projectedProgressMs);
        long renderMs = timing.ComputeRenderPositionMs(clockTargetMs, DURATION_MS);

        // Assert
        clockTargetMs.Should().Be(projectedProgressMs - latencyMs);
        renderMs.Should().Be(expectedRenderMs);
    }

    [Fact]
    public void ClockTarget_NeverGoesBelowTrackStart()
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);

        // Act & Assert - compensation larger than the position pins to zero
        timing.ComputeClockTargetMs(150).Should().Be(0);
    }

    [Theory]
    [InlineData(1000, -5000, 0)]
    [InlineData(239000, 5000, DURATION_MS)]
    public void RenderPosition_IsClampedToTheTrack(long clockPositionMs, int trackOffsetMs, long expectedRenderMs)
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);
        timing.ApplyTrackOffset(new TrackOffsetPayload { TrackId = TRACK_ID, OffsetMs = trackOffsetMs });

        // Act & Assert
        timing.ComputeRenderPositionMs(clockPositionMs, DURATION_MS).Should().Be(expectedRenderMs);
    }

    [Fact]
    public void LatencyCompensation_StartsFromPreferences()
    {
        // Arrange & Act
        (_, PlaybackTiming timing, _) = CreateTiming(400);

        // Assert
        timing.LatencyCompensationMs.Should().Be(400);
        timing.CalibrationText.Should().Be("400 ms");
    }

    [Fact]
    public void AdjustLatencyCompensation_StepsClampsAndPersists()
    {
        // Arrange
        (_, PlaybackTiming timing, ClientPreferences preferences) = CreateTiming(DEFAULT_LATENCY_MS);

        // Act & Assert - one step earlier
        timing.AdjustLatencyCompensation(-PlaybackTiming.LATENCY_STEP_MS);
        timing.LatencyCompensationMs.Should().Be(150);
        timing.CalibrationText.Should().Be("150 ms");
        preferences.LatencyCompensationMs.Should().Be(150);

        // Act & Assert - clamped at both bounds
        timing.AdjustLatencyCompensation(-5000);
        timing.LatencyCompensationMs.Should().Be(ClientPreferences.MIN_LATENCY_COMPENSATION_MS);

        timing.AdjustLatencyCompensation(5000);
        timing.LatencyCompensationMs.Should().Be(ClientPreferences.MAX_LATENCY_COMPENSATION_MS);
        timing.CalibrationText.Should().Be("1000 ms");
        preferences.LatencyCompensationMs.Should().Be(ClientPreferences.MAX_LATENCY_COMPENSATION_MS);
    }

    [Fact]
    public void AdjustLatencyCompensation_ShiftsTheClockTarget()
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);

        // Act
        timing.AdjustLatencyCompensation(PlaybackTiming.LATENCY_STEP_MS);

        // Assert - more compensation renders later
        timing.ComputeClockTargetMs(60000).Should().Be(59750);
    }

    [Fact]
    public void AdjustLatencyCompensation_RaisesChangeNotifications()
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);
        List<string?> raised = new();
        timing.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        timing.AdjustLatencyCompensation(PlaybackTiming.LATENCY_STEP_MS);

        // Assert
        raised.Should().Contain(new[] { nameof(PlaybackTiming.LatencyCompensationMs), nameof(PlaybackTiming.CalibrationText) });
    }

    [Fact]
    public void AdjustLatencyCompensation_AtTheBound_RaisesNothing()
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(ClientPreferences.MAX_LATENCY_COMPENSATION_MS);
        List<string?> raised = new();
        timing.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        timing.AdjustLatencyCompensation(PlaybackTiming.LATENCY_STEP_MS);

        // Assert
        raised.Should().BeEmpty();
    }

    [Fact]
    public async Task NudgeOffset_UpdatesTextAndSavesForTheCurrentTrack()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);
        List<string?> raised = new();
        timing.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        await timing.NudgeOffsetAsync(OFFSET_NUDGE_MS);

        // Assert
        timing.TrackOffsetMs.Should().Be(OFFSET_NUDGE_MS);
        timing.OffsetText.Should().Be("+0.5s");
        raised.Should().Contain(nameof(PlaybackTiming.OffsetText));
        client.Verify(c => c.SetTrackOffsetAsync(TRACK_ID, OFFSET_NUDGE_MS), Times.Once);
    }

    [Fact]
    public async Task NudgeOffset_ClampsToThirtySecondsEitherWay()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);

        // Act & Assert
        await timing.NudgeOffsetAsync(45000);
        timing.TrackOffsetMs.Should().Be(PlaybackTiming.MAX_TRACK_OFFSET_MS);
        timing.OffsetText.Should().Be("+30.0s");

        await timing.NudgeOffsetAsync(-90000);
        timing.TrackOffsetMs.Should().Be(PlaybackTiming.MIN_TRACK_OFFSET_MS);
        timing.OffsetText.Should().Be("-30.0s");
        client.Verify(c => c.SetTrackOffsetAsync(TRACK_ID, PlaybackTiming.MIN_TRACK_OFFSET_MS), Times.Once);
    }

    [Fact]
    public async Task NudgeOffset_WithoutATrack_IsANoOp()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS, trackId: null);

        // Act
        await timing.NudgeOffsetAsync(OFFSET_NUDGE_MS);

        // Assert
        timing.TrackOffsetMs.Should().Be(0);
        timing.OffsetText.Should().Be("+0.0s");
        client.Verify(c => c.SetTrackOffsetAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ResetOffset_ZeroesAndSavesForTheCurrentTrack()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);
        await timing.NudgeOffsetAsync(OFFSET_NUDGE_MS);

        // Act
        await timing.ResetOffsetAsync();

        // Assert
        timing.TrackOffsetMs.Should().Be(0);
        timing.OffsetText.Should().Be("+0.0s");
        client.Verify(c => c.SetTrackOffsetAsync(TRACK_ID, 0), Times.Once);
    }

    [Fact]
    public async Task SetSyncOffset_SavesWithoutChangingTheLocalOffset()
    {
        // Arrange - the server echoes the saved offset back through ApplyTrackOffset
        (Mock<ISignalRPlaybackClient> client, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);

        // Act
        await timing.SetSyncOffsetAsync(1200);

        // Assert
        client.Verify(c => c.SetTrackOffsetAsync(TRACK_ID, 1200), Times.Once);
        timing.TrackOffsetMs.Should().Be(0);
    }

    [Fact]
    public void ApplyTrackOffset_ForTheCurrentTrack_UpdatesOffsetAndText()
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);

        // Act
        timing.ApplyTrackOffset(new TrackOffsetPayload { TrackId = TRACK_ID, OffsetMs = -1200 });

        // Assert
        timing.TrackOffsetMs.Should().Be(-1200);
        timing.OffsetText.Should().Be("-1.2s");
    }

    [Fact]
    public void ApplyTrackOffset_ForAnotherTrackOrNull_IsIgnored()
    {
        // Arrange
        (_, PlaybackTiming timing, _) = CreateTiming(DEFAULT_LATENCY_MS);

        // Act
        timing.ApplyTrackOffset(new TrackOffsetPayload { TrackId = OTHER_TRACK_ID, OffsetMs = 900 });
        timing.ApplyTrackOffset(null);

        // Assert
        timing.TrackOffsetMs.Should().Be(0);
        timing.OffsetText.Should().Be("+0.0s");
    }

    private static (Mock<ISignalRPlaybackClient> Client, PlaybackTiming Timing, ClientPreferences Preferences) CreateTiming(
        int latencyMs,
        string? trackId = TRACK_ID)
    {
        Mock<ISignalRPlaybackClient> client = new();
        client.Setup(c => c.SetTrackOffsetAsync(It.IsAny<string>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        ClientPreferences preferences = new(new InMemorySettingsStore())
        {
            LatencyCompensationMs = latencyMs,
        };
        PlaybackTiming timing = new(client.Object, preferences, () => trackId);
        return (client, timing, preferences);
    }
}
