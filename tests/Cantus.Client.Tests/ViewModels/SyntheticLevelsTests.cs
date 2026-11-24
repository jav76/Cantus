using System;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class SyntheticLevelsTests
{
    private const int BAND_COUNT = 48;

    [Fact]
    public void Fill_SameTrackAndPosition_GivesTheSameLevels()
    {
        // Arrange
        float[] first = new float[BAND_COUNT];
        float[] second = new float[BAND_COUNT];

        // Act
        SyntheticLevels.Fill(first, "track-a", 61_234);
        SyntheticLevels.Fill(second, "track-a", 61_234);

        // Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void Fill_DifferentTracks_GiveDifferentPatterns()
    {
        // Arrange
        float[] first = new float[BAND_COUNT];
        float[] second = new float[BAND_COUNT];

        // Act
        SyntheticLevels.Fill(first, "track-a", 30_000);
        SyntheticLevels.Fill(second, "track-b", 30_000);

        // Assert
        second.Should().NotEqual(first);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 500)]
    [InlineData("track-a", -2_000)]
    [InlineData("track-a", 3_600_000)]
    public void Fill_AnyInput_StaysWithinZeroToOne(string? trackId, long positionMs)
    {
        // Arrange
        float[] bands = new float[BAND_COUNT];

        // Act
        SyntheticLevels.Fill(bands, trackId, positionMs);

        // Assert
        bands.Should().OnlyContain(level => level >= 0 && level <= 1);
    }

    [Fact]
    public void Fill_OnTheBeat_LowBandsAreLouderThanJustBeforeIt()
    {
        // Arrange
        const string trackId = "track-a";
        double beatMs = 60000.0 / SyntheticLevels.GetBeatsPerMinute(trackId);
        float[] bands = new float[BAND_COUNT];
        double onBeatTotal = 0;
        double beforeBeatTotal = 0;
        const int beatCount = 64;

        // Act: average over many beats so the per-band noise evens out.
        for (int beat = 1; beat <= beatCount; beat++)
        {
            SyntheticLevels.Fill(bands, trackId, (long)Math.Ceiling(beatMs * beat));
            onBeatTotal += bands[0];
            SyntheticLevels.Fill(bands, trackId, (long)Math.Floor(beatMs * (beat + 1)) - 5);
            beforeBeatTotal += bands[0];
        }

        // Assert
        (onBeatTotal / beatCount).Should().BeGreaterThan((beforeBeatTotal / beatCount) + 0.2);
    }

    [Fact]
    public void GetBeatsPerMinute_IsStableAndInAMusicalRange()
    {
        // Act
        double bpm = SyntheticLevels.GetBeatsPerMinute("track-a");

        // Assert
        bpm.Should().Be(SyntheticLevels.GetBeatsPerMinute("track-a"));
        bpm.Should().BeInRange(84, 143);
    }

    [Fact]
    public void StableHash_IsTheFnv1aOfTheString()
    {
        SyntheticLevels.StableHash(null).Should().Be(2166136261);
        SyntheticLevels.StableHash("a").Should().Be(0xE40C292C);
    }
}
