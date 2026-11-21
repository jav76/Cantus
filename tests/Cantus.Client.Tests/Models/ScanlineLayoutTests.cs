using System;
using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class ScanlineLayoutTests
{
    private const double PERIOD = 4;

    [Theory]
    [InlineData(1080, 270)]
    [InlineData(1081, 271)]
    [InlineData(4, 1)]
    [InlineData(1, 1)]
    public void GetLineCount_CoversTheExtent(double extent, int expected)
    {
        // Act
        int count = ScanlineLayout.GetLineCount(extent, PERIOD);

        // Assert
        count.Should().Be(expected);
        (count * PERIOD).Should().BeGreaterThanOrEqualTo(extent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GetLineCount_UnusableExtent_IsZero(double extent)
    {
        // Act & Assert
        ScanlineLayout.GetLineCount(extent, PERIOD).Should().Be(0);
    }

    [Fact]
    public void GetLineCount_NonPositivePeriod_Throws()
    {
        // Act
        Action count = () => ScanlineLayout.GetLineCount(100, 0);

        // Assert
        count.Should().Throw<ArgumentOutOfRangeException>();
    }
}
