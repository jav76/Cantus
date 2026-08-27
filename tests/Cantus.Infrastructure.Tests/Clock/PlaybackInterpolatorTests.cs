using Cantus.Core.Models;
using Cantus.Infrastructure.Clock;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Clock;

public class PlaybackInterpolatorTests
{
    [Fact]
    public void CalculateCurrentPosition_WhenPlaying_AdvancesSmoothlyWithTime()
    {
        FakeTimeProvider fakeTime = new();
        DateTimeOffset startTime = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(startTime);

        PlaybackInterpolator interpolator = new(fakeTime, Options.Create(new PlaybackInterpolatorOptions()));

        PlaybackState state = new()
        {
            CurrentTrack = new TrackInfo
            {
                Id = "track1",
                Title = "Song",
                Artist = "Artist",
                Duration = TimeSpan.FromMinutes(3)
            },
            Progress = TimeSpan.FromSeconds(10),
            IsPlaying = true,
            TimestampUtc = startTime
        };

        // At time 0
        TimeSpan pos0 = interpolator.CalculateCurrentPosition(state, TimeSpan.Zero);
        pos0.Should().Be(TimeSpan.FromSeconds(10));

        // Advance time by 3.5 seconds
        fakeTime.Advance(TimeSpan.FromSeconds(3.5));
        TimeSpan pos1 = interpolator.CalculateCurrentPosition(state, TimeSpan.Zero);
        pos1.Should().Be(TimeSpan.FromSeconds(13.5));
    }

    [Fact]
    public void CalculateCurrentPosition_WhenPaused_DoesNotAdvance()
    {
        FakeTimeProvider fakeTime = new();
        DateTimeOffset startTime = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(startTime);

        PlaybackInterpolator interpolator = new(fakeTime, Options.Create(new PlaybackInterpolatorOptions()));

        PlaybackState state = new()
        {
            CurrentTrack = new TrackInfo
            {
                Id = "track1",
                Title = "Song",
                Artist = "Artist",
                Duration = TimeSpan.FromMinutes(3)
            },
            Progress = TimeSpan.FromSeconds(25),
            IsPlaying = false,
            TimestampUtc = startTime
        };

        TimeSpan pos0 = interpolator.CalculateCurrentPosition(state, TimeSpan.Zero);
        pos0.Should().Be(TimeSpan.FromSeconds(25));

        fakeTime.Advance(TimeSpan.FromSeconds(10));
        TimeSpan pos1 = interpolator.CalculateCurrentPosition(state, TimeSpan.Zero);
        pos1.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void CalculateCurrentPosition_WithUserOffset_AppliesOffsetCorrectly()
    {
        FakeTimeProvider fakeTime = new();
        DateTimeOffset startTime = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(startTime);

        PlaybackInterpolator interpolator = new(fakeTime, Options.Create(new PlaybackInterpolatorOptions()));

        PlaybackState state = new()
        {
            CurrentTrack = new TrackInfo
            {
                Id = "track1",
                Title = "Song",
                Artist = "Artist",
                Duration = TimeSpan.FromMinutes(3)
            },
            Progress = TimeSpan.FromSeconds(30),
            IsPlaying = false,
            TimestampUtc = startTime
        };

        TimeSpan pos = interpolator.CalculateCurrentPosition(state, TimeSpan.FromMilliseconds(500));
        pos.Should().Be(TimeSpan.FromMilliseconds(30500));
    }

    [Fact]
    public void CalculateCurrentPosition_WhenSeekOccurs_SnapsDirectlyToNewPosition()
    {
        FakeTimeProvider fakeTime = new();
        DateTimeOffset startTime = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(startTime);

        PlaybackInterpolator interpolator = new(
            fakeTime,
            Options.Create(new PlaybackInterpolatorOptions { SeekThresholdMs = 2000 }));

        PlaybackState state1 = new()
        {
            CurrentTrack = new TrackInfo
            {
                Id = "track1",
                Title = "Song",
                Artist = "Artist",
                Duration = TimeSpan.FromMinutes(5)
            },
            Progress = TimeSpan.FromSeconds(10),
            IsPlaying = true,
            TimestampUtc = startTime
        };

        interpolator.CalculateCurrentPosition(state1, TimeSpan.Zero).Should().Be(TimeSpan.FromSeconds(10));

        // User jumps ahead by 60 seconds (Seek)
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        DateTimeOffset seekTime = fakeTime.GetUtcNow();

        PlaybackState state2 = new()
        {
            CurrentTrack = state1.CurrentTrack,
            Progress = TimeSpan.FromSeconds(70),
            IsPlaying = true,
            TimestampUtc = seekTime
        };

        TimeSpan posAfterSeek = interpolator.CalculateCurrentPosition(state2, TimeSpan.Zero);
        posAfterSeek.Should().Be(TimeSpan.FromSeconds(70));
    }

    [Fact]
    public void CalculateCurrentPosition_WhenExceedingDuration_ClampsToDuration()
    {
        FakeTimeProvider fakeTime = new();
        DateTimeOffset startTime = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(startTime);

        PlaybackInterpolator interpolator = new(fakeTime, Options.Create(new PlaybackInterpolatorOptions()));

        PlaybackState state = new()
        {
            CurrentTrack = new TrackInfo
            {
                Id = "track1",
                Title = "Song",
                Artist = "Artist",
                Duration = TimeSpan.FromSeconds(100)
            },
            Progress = TimeSpan.FromSeconds(95),
            IsPlaying = true,
            TimestampUtc = startTime
        };

        fakeTime.Advance(TimeSpan.FromSeconds(15));
        TimeSpan pos = interpolator.CalculateCurrentPosition(state, TimeSpan.Zero);
        pos.Should().Be(TimeSpan.FromSeconds(100));
    }
}
