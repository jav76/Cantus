using System;
using System.Collections.Generic;
using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class PriorityFitLayoutTests
{
    private static readonly double[] PILL_WIDTHS = { 95, 88, 95, 128 };

    [Theory]
    [InlineData(1000.0, 4)]
    [InlineData(430.0, 4)]
    [InlineData(429.0, 3)]
    [InlineData(294.0, 3)]
    [InlineData(293.0, 2)]
    [InlineData(191.0, 2)]
    [InlineData(190.0, 1)]
    [InlineData(95.0, 1)]
    [InlineData(94.0, 0)]
    [InlineData(0.0, 0)]
    public void CountFitting_KeepsLeadingItemsThatFit(double available, int expected)
    {
        PriorityFitLayout.CountFitting(PILL_WIDTHS, 8, available).Should().Be(expected);
    }

    [Fact]
    public void CountFitting_InfiniteWidth_KeepsEverything()
    {
        PriorityFitLayout.CountFitting(PILL_WIDTHS, 8, double.PositiveInfinity).Should().Be(4);
    }

    [Fact]
    public void CountFitting_NeverSkipsAheadToASmallerLowerPriorityItem()
    {
        double[] widths = { 50, 300, 20 };

        PriorityFitLayout.CountFitting(widths, 8, 100).Should().Be(1);
    }

    [Fact]
    public void CountFitting_CollapsedItemsTakeNoSpacing()
    {
        double[] widths = { 95, 0, 88 };

        PriorityFitLayout.CountFitting(widths, 8, 191).Should().Be(3);
    }

    [Fact]
    public void CountFitting_ToleratesSubPixelRounding()
    {
        double[] widths = { 100.2 };

        PriorityFitLayout.CountFitting(widths, 8, 100).Should().Be(1);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 95.0)]
    [InlineData(2, 191.0)]
    [InlineData(4, 430.0)]
    [InlineData(9, 430.0)]
    public void GetUsedWidth_AddsSpacingBetweenShownItems(int count, double expected)
    {
        PriorityFitLayout.GetUsedWidth(PILL_WIDTHS, 8, count).Should().Be(expected);
    }

    [Fact]
    public void GetUsedWidth_IgnoresCollapsedItems()
    {
        double[] widths = { 0, 95, 0, 88 };

        PriorityFitLayout.GetUsedWidth(widths, 8, 4).Should().Be(191);
    }

    [Fact]
    public void NullWidths_Throw()
    {
        Action count = () => PriorityFitLayout.CountFitting(null!, 8, 100);
        Action used = () => PriorityFitLayout.GetUsedWidth(null!, 8, 1);

        count.Should().Throw<ArgumentNullException>();
        used.Should().Throw<ArgumentNullException>();
    }
}
