using System;
using System.Collections.Generic;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricLineViewModelTests
{
    [Fact]
    public void UpdateLineProgress_TracksElapsedFractionOfLineDuration()
    {
        // Arrange - a 4-second line starting at 10s
        LyricLineViewModel line = new() { TimestampMs = 10000, Text = "sing along", DurationMs = 4000 };
        line.IsActive = true;

        // Act & Assert
        line.UpdateLineProgress(10000);
        line.LineProgressFraction.Should().Be(0.0);

        line.UpdateLineProgress(12000);
        line.LineProgressFraction.Should().BeApproximately(0.5, 0.001);

        line.UpdateLineProgress(13000);
        line.LineProgressFraction.Should().BeApproximately(0.75, 0.001);

        line.UpdateLineProgress(14000);
        line.LineProgressFraction.Should().Be(1.0);
    }

    [Fact]
    public void UpdateLineProgress_ClampsOutsideLineBounds()
    {
        // Arrange
        LyricLineViewModel line = new() { TimestampMs = 10000, Text = "sing along", DurationMs = 4000 };
        line.IsActive = true;

        // Act & Assert - a negative user offset or overshoot never escapes [0, 1]
        line.UpdateLineProgress(9000);
        line.LineProgressFraction.Should().Be(0.0);

        line.UpdateLineProgress(99000);
        line.LineProgressFraction.Should().Be(1.0);
    }

    [Fact]
    public void UpdateLineProgress_WhenInactiveOrDurationUnknown_IsANoOp()
    {
        // Arrange - inactive line
        LyricLineViewModel inactiveLine = new() { TimestampMs = 10000, Text = "sing", DurationMs = 4000 };

        // Act
        inactiveLine.UpdateLineProgress(12000);

        // Assert
        inactiveLine.LineProgressFraction.Should().Be(0.0);

        // Arrange - last line of the song has no honest duration
        LyricLineViewModel lastLine = new() { TimestampMs = 10000, Text = "the end", DurationMs = null };
        lastLine.IsActive = true;

        // Act
        lastLine.UpdateLineProgress(12000);

        // Assert
        lastLine.LineProgressFraction.Should().Be(0.0);
        lastLine.LineProgressVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void LineProgressVisibility_RequiresActiveLineWithKnownDuration()
    {
        // Arrange
        LyricLineViewModel line = new() { TimestampMs = 0, Text = "words", DurationMs = 3000 };

        // Act & Assert
        line.LineProgressVisibility.Should().Be(Visibility.Collapsed);

        line.IsActive = true;
        line.LineProgressVisibility.Should().Be(Visibility.Visible);

        line.IsActive = false;
        line.LineProgressVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Deactivation_ResetsLineProgress()
    {
        // Arrange
        LyricLineViewModel line = new() { TimestampMs = 0, Text = "words here", DurationMs = 4000 };
        line.IsActive = true;
        line.UpdateLineProgress(2000);
        line.LineProgressFraction.Should().BeGreaterThan(0.0);

        // Act
        line.IsActive = false;

        // Assert
        line.LineProgressFraction.Should().Be(0.0);
    }

    [Fact]
    public void SetKaraokeEnabled_False_HidesUnderlineAndResetsProgress()
    {
        // Arrange
        LyricLineViewModel line = new() { TimestampMs = 0, Text = "sing along", DurationMs = 4000 };
        line.IsActive = true;
        line.UpdateLineProgress(2000);
        line.LineProgressVisibility.Should().Be(Visibility.Visible);
        line.LineProgressFraction.Should().BeGreaterThan(0.0);

        // Act
        line.SetKaraokeEnabled(false);

        // Assert
        line.LineProgressVisibility.Should().Be(Visibility.Collapsed);
        line.LineProgressFraction.Should().Be(0.0);

        // Act - re-enable restores visibility for the active line
        line.SetKaraokeEnabled(true);
        line.LineProgressVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void ToggleKaraokeMode_PropagatesToAllLinesAndPersistsHeadlessSafely()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "first line" },
                new() { TimestampMs = 5000, Text = "second line" }
            }
        });
        vm.IsKaraokeModeEnabled.Should().BeTrue();
        vm.LyricLines[0].IsActive = true;

        // Act
        vm.ToggleKaraokeMode();

        // Assert
        vm.IsKaraokeModeEnabled.Should().BeFalse();
        vm.LyricLines[0].LineProgressVisibility.Should().Be(Visibility.Collapsed);

        // Act - toggle back
        vm.ToggleKaraokeMode();
        vm.IsKaraokeModeEnabled.Should().BeTrue();
        vm.LyricLines[0].LineProgressVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void OnLyricsReceived_NotifiesKaraokeToggleVisibility()
    {
        // Arrange - the toggle button binds KaraokeToggleVisibility at page load
        // (no lyrics -> Collapsed); receiving lyrics MUST notify it or the
        // button never appears. Regression: the notification was missing.
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());
        List<string> notified = new();
        vm.PropertyChanged += (s, e) => notified.Add(e.PropertyName ?? string.Empty);

        // Act
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload> { new() { TimestampMs = 1000, Text = "a line of words" } }
        });

        // Assert
        notified.Should().Contain(nameof(LyricsViewModel.KaraokeToggleVisibility));
        vm.KaraokeToggleVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void OnLyricsReceived_PopulatesLineDurationsFromNextLine()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());
        LyricsPayload payload = new()
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "first line" },
                new() { TimestampMs = 5000, Text = "second line" },
                new() { TimestampMs = 9500, Text = "the last line" }
            }
        };

        // Act
        client.RaiseLyricsReceived(payload);

        // Assert
        vm.LyricLines.Should().HaveCount(3);
        vm.LyricLines[0].DurationMs.Should().Be(4000);
        vm.LyricLines[1].DurationMs.Should().Be(4500);
        vm.LyricLines[2].DurationMs.Should().BeNull(); // last line: no honest end time
    }

    [Fact]
    public void ComputeLineFillDurationMs_KeepsExactGapForContinuousSinging()
    {
        // Gaps below the instrumental-break threshold ARE the sung duration -
        // the bar completing exactly as the next line activates is the point.
        LyricsViewModel.ComputeLineFillDurationMs("a normal lyric line", 4000).Should().Be(4000);
        LyricsViewModel.ComputeLineFillDurationMs("a normal lyric line", 7999).Should().Be(7999);
    }

    [Fact]
    public void ComputeLineFillDurationMs_CapsBreakLengthGapsAtSingingEstimate()
    {
        // "Just beyond the shadow of my dreams" (35 chars) followed by a long
        // musical passage: the bar should finish near the end of the words,
        // not crawl through the whole instrumental tail.
        long fill = LyricsViewModel.ComputeLineFillDurationMs("Just beyond the shadow of my dreams", 15000);

        fill.Should().Be(600 + (35 * 90));
        fill.Should().BeLessThan(5000);
    }

    [Fact]
    public void ComputeLineFillDurationMs_ClampsEstimateToSaneBounds()
    {
        // Very short text never produces a sub-perceptual flash...
        LyricsViewModel.ComputeLineFillDurationMs("Oh", 12000).Should().Be(1500);

        // ...and a very long line's estimate never exceeds the actual gap.
        string longLine = new('x', 200);
        LyricsViewModel.ComputeLineFillDurationMs(longLine, 9000).Should().Be(9000);
    }

    [Fact]
    public void ComputeLineFillDurationMs_InstrumentalPlaceholderKeepsFullGap()
    {
        // A "♪" line IS the interlude: filling over the whole gap genuinely
        // tracks progress through it.
        LyricsViewModel.ComputeLineFillDurationMs("♪", 20000).Should().Be(20000);
    }
}
