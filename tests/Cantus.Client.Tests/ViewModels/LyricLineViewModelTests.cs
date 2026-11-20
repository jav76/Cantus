using System;
using System.Collections.Generic;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
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

    [Fact]
    public void UpdateLineProgress_WithSyllables_FollowsRealWordTiming()
    {
        // Arrange - the line's gap to the next line (6s) is longer than its singing
        LyricLineViewModel line = new()
        {
            TimestampMs = 10000,
            Text = "hold on",
            DurationMs = 6000,
            Syllables = new List<SyllableTiming>
            {
                new(10000, 500, "hold "),
                new(12000, 1000, "on"),
            },
        };
        line.IsActive = true;

        // Act & Assert - fills "hold", rests through the gap, then fills "on"
        line.UpdateLineProgress(10250);
        line.LineProgressFraction.Should().BeApproximately(2.0 / 7, 0.001);

        line.UpdateLineProgress(11000);
        line.LineProgressFraction.Should().BeApproximately(4.0 / 7, 0.001);

        line.UpdateLineProgress(11900);
        line.LineProgressFraction.Should().BeApproximately(4.0 / 7, 0.001);

        line.UpdateLineProgress(12500);
        line.LineProgressFraction.Should().BeApproximately(6.0 / 7, 0.001);

        line.UpdateLineProgress(13000);
        line.LineProgressFraction.Should().Be(1.0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdateLineProgress_WithoutUsableSyllables_KeepsElapsedTimeFill(bool hasEmptySyllableList)
    {
        // Arrange
        LyricLineViewModel line = new()
        {
            TimestampMs = 10000,
            Text = "sing along",
            DurationMs = 4000,
            Syllables = hasEmptySyllableList ? new List<SyllableTiming>() : null,
        };
        line.IsActive = true;

        // Act
        line.UpdateLineProgress(11000);

        // Assert
        line.HasWordTiming.Should().BeFalse();
        line.LineProgressFraction.Should().BeApproximately(0.25, 0.001);
    }

    [Fact]
    public void LineProgressVisibility_FinalLineWithSyllables_ShowsTheFill()
    {
        // Arrange - the last line has no next line, but its syllables give a real end
        LyricLineViewModel line = new()
        {
            TimestampMs = 10000,
            Text = "the end",
            DurationMs = null,
            Syllables = new List<SyllableTiming> { new(10000, 400, "the "), new(10400, 600, "end") },
        };

        // Act
        line.IsActive = true;
        line.UpdateLineProgress(11000);

        // Assert
        line.HasWordTiming.Should().BeTrue();
        line.LineProgressVisibility.Should().Be(Visibility.Visible);
        line.LineProgressFraction.Should().Be(1.0);
    }

    [Fact]
    public void OnLyricsReceived_CarriesSyllablesIntoLineViewModels()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());

        // Act
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new()
                {
                    TimestampMs = 1000,
                    Text = "We're no",
                    Syllables = new List<LyricSyllablePayload>
                    {
                        new() { TimestampMs = 1000, DurationMs = 420, Text = "We're " },
                        new() { TimestampMs = 1420, DurationMs = 310, Text = "no" },
                    },
                },
                new() { TimestampMs = 5000, Text = "plain line" },
            },
        });

        // Assert
        vm.LyricLines[0].HasWordTiming.Should().BeTrue();
        vm.LyricLines[0].Syllables.Should().Equal(
            new SyllableTiming(1000, 420, "We're "),
            new SyllableTiming(1420, 310, "no"));
        vm.LyricLines[1].HasWordTiming.Should().BeFalse();
        vm.LyricLines[1].Syllables.Should().BeNull();
        vm.LyricLines[1].DurationMs.Should().BeNull();
    }

    [Fact]
    public void OnLyricsReceived_ZeroDurationSyllables_RunUntilTheNextSyllableOrTheLineFillEnd()
    {
        // Arrange - enhanced LRC word tags carry starts only, so the last word has no duration
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());

        // Act
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new()
                {
                    TimestampMs = 1000,
                    Text = "one two",
                    Syllables = new List<LyricSyllablePayload>
                    {
                        new() { TimestampMs = 1000, DurationMs = 0, Text = "one " },
                        new() { TimestampMs = 1800, DurationMs = 0, Text = "two" },
                    },
                },
                new() { TimestampMs = 4000, Text = "next" },
            },
        });

        // Assert - "one" runs to "two", and "two" runs to the line's fill end
        vm.LyricLines[0].Syllables.Should().Equal(
            new SyllableTiming(1000, 800, "one "),
            new SyllableTiming(1800, 2200, "two"));
    }

    [Fact]
    public void AdvancePlayback_SyllableLineUsesRealTiming_PlainLineUsesElapsedTime()
    {
        // Arrange - paused with no latency, so the render position is the poll position
        Mock<ISignalRPlaybackClient> client = new();
        LyricsViewModel vm = new(client.Object, new ThemeManager(), new ResponsiveLayoutManager());
        vm.Timing.LatencyCompensationMs = 0;
        vm.IsKaraokeModeEnabled = true;
        client.Raise(c => c.LyricsReceived += null, new LyricsPayload
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new()
                {
                    TimestampMs = 10000,
                    Text = "hold on",
                    Syllables = new List<LyricSyllablePayload>
                    {
                        new() { TimestampMs = 10000, DurationMs = 500, Text = "hold " },
                        new() { TimestampMs = 12000, DurationMs = 1000, Text = "on" },
                    },
                },
                new() { TimestampMs = 14000, Text = "plain line" },
                new() { TimestampMs = 18000, Text = "last" },
            },
        });

        // Act & Assert - resting between "hold" and "on", not interpolating through the gap
        RaisePausedAt(client, 11500);
        vm.AdvancePlayback();
        vm.ActiveLineIndex.Should().Be(0);
        vm.LyricLines[0].LineProgressFraction.Should().BeApproximately(4.0 / 7, 0.001);

        // Act & Assert - the plain line keeps the elapsed-time fill
        RaisePausedAt(client, 15000);
        vm.AdvancePlayback();
        vm.ActiveLineIndex.Should().Be(1);
        vm.LyricLines[1].LineProgressFraction.Should().BeApproximately(0.25, 0.001);
    }

    private static void RaisePausedAt(Mock<ISignalRPlaybackClient> client, long progressMs)
    {
        client.Raise(c => c.PlaybackStateReceived += null, new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t1", Title = "Song", Artist = "Artist", DurationMs = 240000 },
            ProgressMs = progressMs,
            IsPlaying = false,
            TimestampUtc = DateTimeOffset.UtcNow,
        });
    }
}
