using System;
using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class FilmGrainLayoutTests
{
    private const double TILE_SIZE = 256;

    [Theory]
    [InlineData(1920, 9)]
    [InlineData(1080, 6)]
    [InlineData(256, 2)]
    [InlineData(257, 3)]
    [InlineData(1, 2)]
    public void GetTileCount_CoversTheExtentPlusOneTileOfShift(double extent, int expected)
    {
        // Act
        int count = FilmGrainLayout.GetTileCount(extent, TILE_SIZE);

        // Assert
        count.Should().Be(expected);
        (count * TILE_SIZE - TILE_SIZE).Should().BeGreaterThanOrEqualTo(extent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GetTileCount_WithNoUsableExtent_ReturnsZero(double extent)
    {
        // Act & Assert
        FilmGrainLayout.GetTileCount(extent, TILE_SIZE).Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void GetTileCount_WithInvalidTileSize_Throws(double tileSize)
    {
        // Act
        Action act = () => FilmGrainLayout.GetTileCount(100, tileSize);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, -128)]
    [InlineData(0.999, -255)]
    [InlineData(0.0021, 0)]
    public void GetJitterOffset_MapsSampleToWholePixelShiftTowardsOrigin(double sample, double expected)
    {
        // Act & Assert
        FilmGrainLayout.GetJitterOffset(sample, TILE_SIZE).Should().Be(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(-0.5)]
    public void GetJitterOffset_OutOfRangeSamples_StayWithinOneTile(double sample)
    {
        // Act
        double offset = FilmGrainLayout.GetJitterOffset(sample, TILE_SIZE);

        // Assert
        offset.Should().BeInRange(-TILE_SIZE + 1, 0);
    }

    [Theory]
    [InlineData(-1, 3, 0)]
    [InlineData(0, 3, 1)]
    [InlineData(1, 3, 2)]
    [InlineData(2, 3, 0)]
    [InlineData(0, 1, 0)]
    public void GetNextLayerIndex_AdvancesAndWraps(int current, int count, int expected)
    {
        // Act & Assert
        FilmGrainLayout.GetNextLayerIndex(current, count).Should().Be(expected);
    }

    [Fact]
    public void GetNextLayerIndex_WithNoLayers_Throws()
    {
        // Act
        Action act = () => FilmGrainLayout.GetNextLayerIndex(0, 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
