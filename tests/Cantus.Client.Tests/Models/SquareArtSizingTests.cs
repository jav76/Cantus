using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class SquareArtSizingTests
{
    private const double GUTTER = 15.0;

    [Fact]
    public void GetNextHeight_FirstLayout_MatchesWidth()
    {
        SquareArtSizing.GetNextHeight(double.NaN, 330, false, 200, 800).Should().Be(330);
    }

    [Fact]
    public void GetNextHeight_WithinTolerance_KeepsHeight()
    {
        SquareArtSizing.GetNextHeight(330, 330.3, false, 700, 800).Should().BeNull();
    }

    [Fact]
    public void GetNextHeight_ZeroWidth_KeepsHeight()
    {
        SquareArtSizing.GetNextHeight(330, 0, false, 700, 800).Should().BeNull();
    }

    [Fact]
    public void GetNextHeight_Shrinking_AlwaysApplies()
    {
        SquareArtSizing.GetNextHeight(330, 330 - GUTTER, true, 810, 800).Should().Be(330 - GUTTER);
    }

    [Fact]
    public void GetNextHeight_GrowingWhenItStillFits_Applies()
    {
        SquareArtSizing.GetNextHeight(300, 330, false, 700, 800).Should().Be(330);
    }

    [Fact]
    public void GetNextHeight_GrowingWithScrollBarAlreadyShown_Applies()
    {
        SquareArtSizing.GetNextHeight(300, 330, true, 900, 800).Should().Be(330);
    }

    [Fact]
    public void GetNextHeight_GrowthThatWouldShowTheScrollBar_IsHeld()
    {
        // The scrollbar just hid after a shrink: growing back would overflow and show it again.
        SquareArtSizing.GetNextHeight(330 - GUTTER, 330, false, 795, 800).Should().BeNull();
    }

    [Fact]
    public void GetNextHeight_ScrollBarGutterCycle_SettlesAfterOneShrink()
    {
        const double viewport = 800;
        const double contentWithoutArt = 480;
        double height = double.NaN;
        int changes = 0;

        for (int pass = 0; pass < 10; pass++)
        {
            double currentArt = double.IsNaN(height) ? 0 : height;
            bool scrollBarShown = contentWithoutArt + currentArt > viewport;
            double width = scrollBarShown ? 330 - GUTTER : 330;
            double? next = SquareArtSizing.GetNextHeight(height, width, scrollBarShown, contentWithoutArt + currentArt, viewport);
            if (next is double applied)
            {
                height = applied;
                changes++;
            }
        }

        changes.Should().BeLessThanOrEqualTo(2);
        height.Should().Be(330 - GUTTER);
    }

    private const double MIN_ART = 120.0;

    [Fact]
    public void GetHeightCap_TallViewport_LeavesRoomForTheRestOfThePanel()
    {
        // 844 viewport, 330 art + 400 of text and controls: the art may grow to 844 - 400.
        SquareArtSizing.GetHeightCap(844, 730, 330, MIN_ART).Should().Be(444);
    }

    [Fact]
    public void GetHeightCap_ShortViewport_StopsAtTheMinimum()
    {
        SquareArtSizing.GetHeightCap(450, 730, 330, MIN_ART).Should().Be(MIN_ART);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GetHeightCap_NoMeasuredViewport_IsUncapped(double viewport)
    {
        SquareArtSizing.GetHeightCap(viewport, 730, 330, MIN_ART).Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void GetHeightCap_ArtNotMeasuredYet_CountsTheWholeContentAsTheRest()
    {
        SquareArtSizing.GetHeightCap(844, 400, double.NaN, MIN_ART).Should().Be(444);
    }

    [Fact]
    public void GetHeightCap_ApplyingTheCap_SettlesInOnePass()
    {
        const double viewport = 844;
        const double rest = 400;
        double art = 360;
        double cap = SquareArtSizing.GetHeightCap(viewport, rest + art, art, MIN_ART);

        for (int pass = 0; pass < 5; pass++)
        {
            art = cap;
            SquareArtSizing.GetHeightCap(viewport, rest + art, art, MIN_ART).Should().Be(cap);
        }

        (rest + art).Should().Be(viewport);
    }
}
