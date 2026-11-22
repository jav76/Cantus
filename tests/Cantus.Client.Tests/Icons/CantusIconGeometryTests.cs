using System;
using System.Globalization;
using System.Linq;
using Cantus.Client.Icons;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Icons;

public sealed class CantusIconGeometryTests
{
    public static TheoryData<CantusIconKind> AllKinds()
    {
        TheoryData<CantusIconKind> data = new();
        foreach (CantusIconKind kind in Enum.GetValues<CantusIconKind>())
        {
            data.Add(kind);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Paths_AreSpaceSeparatedCommandsAndNumbersInsideThe256Box(CantusIconKind kind)
    {
        string[] tokens = CantusIconPaths.Get(kind).Split(' ');

        tokens[0].Should().Be("M");
        foreach (string token in tokens)
        {
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                "MmLlHhVvCcSsQqTtAaZz".Should().Contain(token);
            }
            else
            {
                double value = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
                value.Should().BeInRange(-256, 256);
            }
        }
    }

    [Fact]
    public void GetPathMarkup_PrefixesTheNonzeroFillRule()
    {
        CantusIconGeometry.GetPathMarkup(CantusIconKind.Close, 16).Should().StartWith("F1 M ");
    }

    [Fact]
    public void GetPathMarkup_AtTheSourceSizeKeepsTheNumbers()
    {
        string markup = CantusIconGeometry.GetPathMarkup(CantusIconKind.Close, CantusIconGeometry.SOURCE_SIZE);

        markup.Should().Be("F1 " + CantusIconPaths.Get(CantusIconKind.Close));
    }

    [Fact]
    public void GetPathMarkup_ScalesCoordinatesToTheSize()
    {
        // Phosphor "x" starts at (205.66, 194.34) in the 256 box.
        string markup = CantusIconGeometry.GetPathMarkup(CantusIconKind.Close, 16);

        markup.Should().StartWith("F1 M 12.854 12.146 ");
    }

    [Fact]
    public void GetPathMarkup_ReturnsTheCachedStringForTheSameKindAndSize()
    {
        string first = CantusIconGeometry.GetPathMarkup(CantusIconKind.Play, 14);
        string second = CantusIconGeometry.GetPathMarkup(CantusIconKind.Play, 14);

        second.Should().BeSameAs(first);
        CantusIconGeometry.GetPathMarkup(CantusIconKind.Pause, 14).Should().NotBe(first);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GetPathMarkup_RejectsSizesThatAreNotPositive(double size)
    {
        Action act = () => CantusIconGeometry.GetPathMarkup(CantusIconKind.Play, size);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Scale_KeepsArcRotationAndFlagsIncludingRepeatedArcs()
    {
        string scaled = CantusIconGeometry.Scale("M 0 0 a 8 8 0 0 1 16 16 8 8 45 1 0 -16 -16 Z", 0.5);

        scaled.Should().Be("M 0 0 a 4 4 0 0 1 8 8 4 4 45 1 0 -8 -8 Z");
    }

    [Fact]
    public void Scale_ScalesEveryArgumentOfOtherCommands()
    {
        string scaled = CantusIconGeometry.Scale("M 10 20 H 30 V 40 C 1 2 3 4 5 6 q -2 -4 8 8 L 2 2 4 4", 0.5);

        scaled.Should().Be("M 5 10 H 15 V 20 C 0.5 1 1.5 2 2.5 3 q -1 -2 4 4 L 1 1 2 2");
    }

    [Fact]
    public void Scale_RoundsToThreeDecimalsWithoutNegativeZero()
    {
        CantusIconGeometry.Scale("M 1 -0.0001", 1.0 / 3).Should().Be("M 0.333 0");
    }

    [Fact]
    public void EveryKindHasAPath()
    {
        Enum.GetValues<CantusIconKind>()
            .Select(CantusIconPaths.Get)
            .Should().OnlyContain(path => path.Length > 0);
    }
}
