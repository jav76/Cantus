using System.Collections.Generic;
using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class SyllableFillTimelineTests
{
    private const double PRECISION = 0.0001;

    [Fact]
    public void ComputeFraction_FillsEachWordOverItsOwnDuration()
    {
        // Arrange - "We're no strangers" is 18 characters
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(1000, 400, "We're "),
            new SyllableTiming(1400, 300, "no "),
            new SyllableTiming(1700, 900, "strangers"));

        // Act & Assert
        timeline.ComputeFraction(500).Should().Be(0.0);
        timeline.ComputeFraction(1000).Should().Be(0.0);
        timeline.ComputeFraction(1200).Should().BeApproximately(2.5 / 18, PRECISION);
        timeline.ComputeFraction(1550).Should().BeApproximately(7.0 / 18, PRECISION);
        timeline.ComputeFraction(2150).Should().BeApproximately(13.5 / 18, PRECISION);
        timeline.ComputeFraction(2600).Should().Be(1.0);
        timeline.ComputeFraction(9000).Should().Be(1.0);
    }

    [Fact]
    public void ComputeFraction_HoldsStillThroughAGapBetweenSyllables()
    {
        // Arrange - "hold on" with a 1.5s rest after "hold"
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(0, 500, "hold "),
            new SyllableTiming(2000, 500, "on"));

        // Act & Assert - the fill stops at the end of "hold" until "on" starts
        double atWordEnd = timeline.ComputeFraction(500);
        atWordEnd.Should().BeApproximately(4.0 / 7, PRECISION);
        timeline.ComputeFraction(1000).Should().Be(atWordEnd);
        timeline.ComputeFraction(1999).Should().Be(atWordEnd);
        timeline.ComputeFraction(2250).Should().BeApproximately(6.0 / 7, PRECISION);
    }

    [Fact]
    public void ComputeFraction_HeldNoteFillsSlowlyAcrossItsWholeDuration()
    {
        // Arrange - a short word then a 4s held "ooh"
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(0, 200, "oh "),
            new SyllableTiming(200, 4000, "ooh"));

        // Act & Assert - "oh" is done quickly, "ooh" crawls over four seconds
        timeline.ComputeFraction(200).Should().BeApproximately(3.0 / 6, PRECISION);
        timeline.ComputeFraction(2200).Should().BeApproximately(4.5 / 6, PRECISION);
        timeline.ComputeFraction(4199).Should().BeLessThan(1.0);
        timeline.ComputeFraction(4200).Should().Be(1.0);
    }

    [Fact]
    public void ComputeFraction_CjkLineWithOneSyllablePerCharacter()
    {
        // Arrange - no spaces, every character is its own syllable
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(0, 250, "你"),
            new SyllableTiming(250, 250, "好"),
            new SyllableTiming(500, 250, "世"),
            new SyllableTiming(750, 250, "界"));

        // Act & Assert
        timeline.ComputeFraction(500).Should().BeApproximately(0.5, PRECISION);
        timeline.ComputeFraction(625).Should().BeApproximately(0.625, PRECISION);
        timeline.ComputeFraction(1000).Should().Be(1.0);
    }

    [Fact]
    public void ComputeFraction_TrailingSpaceIsNotSungTime()
    {
        // Arrange - "zulu yankee" is 11 characters; the space belongs to neither word's fill
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(0, 500, "zulu "),
            new SyllableTiming(1000, 500, "yankee "));

        // Act & Assert - "zulu" completes at its own end, the line completes at the last letter
        timeline.ComputeFraction(500).Should().BeApproximately(4.0 / 11, PRECISION);
        timeline.ComputeFraction(999).Should().BeApproximately(4.0 / 11, PRECISION);
        timeline.ComputeFraction(1000).Should().BeApproximately(5.0 / 11, PRECISION);
        timeline.ComputeFraction(1500).Should().Be(1.0);
    }

    [Fact]
    public void ComputeFraction_SplitSyllablesOfOneWordStayContiguous()
    {
        // Arrange - "beautiful" sung as three syllables with no space between them
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(0, 300, "beau"),
            new SyllableTiming(300, 300, "ti"),
            new SyllableTiming(600, 300, "ful"));

        // Act & Assert
        timeline.ComputeFraction(300).Should().BeApproximately(4.0 / 9, PRECISION);
        timeline.ComputeFraction(600).Should().BeApproximately(6.0 / 9, PRECISION);
    }

    [Fact]
    public void ComputeFraction_ZeroDurationSyllableCompletesAtItsStart()
    {
        // Arrange
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(0, 0, "a "),
            new SyllableTiming(1000, 1000, "b"));

        // Act & Assert
        timeline.ComputeFraction(0).Should().BeApproximately(1.0 / 3, PRECISION);
        timeline.ComputeFraction(500).Should().BeApproximately(1.0 / 3, PRECISION);
    }

    [Fact]
    public void EndMs_IsTheLastSyllableEnd()
    {
        // Arrange
        SyllableFillTimeline timeline = Create(
            new SyllableTiming(0, 500, "one "),
            new SyllableTiming(800, 700, "two"));

        // Act & Assert
        timeline.EndMs.Should().Be(1500);
    }

    [Fact]
    public void Create_WithoutUsableSyllables_ReturnsNull()
    {
        SyllableFillTimeline.Create(null).Should().BeNull();
        SyllableFillTimeline.Create(new List<SyllableTiming>()).Should().BeNull();
        SyllableFillTimeline.Create(new List<SyllableTiming> { new(0, 500, " "), new(500, 500, "") })
            .Should()
            .BeNull();
    }

    private static SyllableFillTimeline Create(params SyllableTiming[] syllables)
    {
        SyllableFillTimeline? timeline = SyllableFillTimeline.Create(syllables);
        timeline.Should().NotBeNull();
        return timeline!;
    }
}
