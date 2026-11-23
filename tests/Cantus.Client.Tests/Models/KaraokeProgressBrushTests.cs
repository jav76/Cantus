using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class KaraokeProgressBrushTests
{
    [Fact]
    public void GetColor_UnderDynamicTheme_UsesTheArtworkSecondaryAccent()
    {
        // Arrange
        ColorPalette palette = ColorPalette.EmeraldSynth;

        // Act
        var color = KaraokeProgressBrush.GetColor(ThemeMode.Dynamic, palette);

        // Assert
        color.Should().Be(palette.SecondaryAccent);
    }

    [Theory]
    [InlineData(ThemeMode.MidnightViolet)]
    [InlineData(ThemeMode.EmeraldSynth)]
    [InlineData(ThemeMode.CyberpunkSunset)]
    [InlineData(ThemeMode.NordicSlate)]
    [InlineData(ThemeMode.OLEDMonochrome)]
    [InlineData(ThemeMode.SolarizedDark)]
    public void GetColor_UnderAFixedTheme_KeepsTheDefaultColor(ThemeMode mode)
    {
        // Arrange
        ColorPalette palette = ColorPalette.CyberpunkSunset;

        // Act
        var color = KaraokeProgressBrush.GetColor(mode, palette);

        // Assert
        color.Should().Be(KaraokeProgressBrush.DefaultColor);
    }
}
