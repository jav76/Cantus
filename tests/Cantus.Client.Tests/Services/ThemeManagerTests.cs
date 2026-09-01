using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using FluentAssertions;
using Windows.UI;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ThemeManagerTests
{
    [Fact]
    public void SetThemeMode_PredefinedPalettes_UpdatesActivePaletteCorrectly()
    {
        // Arrange
        ThemeManager tm = new();

        // Act - EmeraldSynth
        tm.SetThemeMode(ThemeMode.EmeraldSynth);

        // Assert
        tm.CurrentMode.Should().Be(ThemeMode.EmeraldSynth);
        tm.ActivePalette.Name.Should().Be("Emerald Synth");
        tm.ActivePalette.PrimaryAccent.Should().Be(Color.FromArgb(255, 16, 185, 129));

        // Act - CyberpunkSunset
        tm.SetThemeMode(ThemeMode.CyberpunkSunset);
        tm.ActivePalette.Name.Should().Be("Cyberpunk Sunset");
        tm.ActivePalette.PrimaryAccent.Should().Be(Color.FromArgb(255, 244, 63, 94));

        // Act - OLEDMonochrome
        tm.SetThemeMode(ThemeMode.OLEDMonochrome);
        tm.ActivePalette.Name.Should().Be("OLED Monochrome");
        tm.ActivePalette.Background.Should().Be(Color.FromArgb(255, 0, 0, 0));

        // Act - SolarizedDark
        tm.SetThemeMode(ThemeMode.SolarizedDark);
        tm.ActivePalette.Name.Should().Be("Solarized Dark");
        tm.ActivePalette.Background.Should().Be(Color.FromArgb(255, 0, 43, 54));
        tm.ActivePalette.PrimaryAccent.Should().Be(Color.FromArgb(255, 38, 139, 210));
    }

    [Fact]
    public void DynamicTheme_WithTrackMetadata_GeneratesHarmoniousPalette()
    {
        // Arrange
        ThemeManager tm = new();
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Act
        tm.UpdateTrackMetadata(
            title: "Blinding Lights",
            artist: "The Weeknd",
            albumArtUrl: "https://i.scdn.co/image/ab67616d0000b2738863bc11d2aa12b54f5aeb36");

        // Assert
        tm.ActivePalette.Name.Should().Contain("Blinding Lights");
        tm.ActivePalette.Background.A.Should().Be(255);
        tm.ActivePalette.PrimaryAccent.A.Should().Be(255);
        tm.ActivePalette.GlowColor.A.Should().Be(60);
    }

    [Fact]
    public void CycleNextTheme_AdvancesThroughAllThemesInOrder()
    {
        // Arrange
        ThemeManager tm = new();
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Act & Assert
        tm.CycleNextTheme();
        tm.CurrentMode.Should().Be(ThemeMode.MidnightViolet);

        tm.CycleNextTheme();
        tm.CurrentMode.Should().Be(ThemeMode.EmeraldSynth);

        tm.CycleNextTheme();
        tm.CurrentMode.Should().Be(ThemeMode.CyberpunkSunset);

        tm.CycleNextTheme();
        tm.CurrentMode.Should().Be(ThemeMode.NordicSlate);

        tm.CycleNextTheme();
        tm.CurrentMode.Should().Be(ThemeMode.OLEDMonochrome);

        tm.CycleNextTheme();
        tm.CurrentMode.Should().Be(ThemeMode.SolarizedDark);

        tm.CycleNextTheme();
        tm.CurrentMode.Should().Be(ThemeMode.Dynamic);
    }

    [Fact]
    public void HslToRgb_CalculatesValidRgbValues()
    {
        // Act - Pure Red (Hue 0, Sat 1.0, Lightness 0.5)
        Color red = ColorExtractionHelper.HslToRgb(0f, 1f, 0.5f);
        red.R.Should().Be(255);
        red.G.Should().Be(0);
        red.B.Should().Be(0);

        // Act - Pure Green (Hue 120, Sat 1.0, Lightness 0.5)
        Color green = ColorExtractionHelper.HslToRgb(120f, 1f, 0.5f);
        green.R.Should().Be(0);
        green.G.Should().Be(255);
        green.B.Should().Be(0);

        // Act - Pure Blue (Hue 240, Sat 1.0, Lightness 0.5)
        Color blue = ColorExtractionHelper.HslToRgb(240f, 1f, 0.5f);
        blue.R.Should().Be(0);
        blue.G.Should().Be(0);
        blue.B.Should().Be(255);
    }

    [Fact]
    public void ThemeManager_PaletteUpdatesAndNotifiesOnThemeChange()
    {
        // Arrange
        ThemeManager tm = new();
        tm.SetThemeMode(ThemeMode.MidnightViolet);
        tm.ActivePalette.Should().Be(ColorPalette.MidnightViolet);

        ColorPalette? notifiedPalette = null;
        tm.PaletteChanged += p => notifiedPalette = p;

        // Act
        tm.SetThemeMode(ThemeMode.EmeraldSynth);

        // Assert
        tm.ActivePalette.Should().Be(ColorPalette.EmeraldSynth);
        tm.ActivePalette.Background.Should().Be(ColorPalette.EmeraldSynth.Background);
        tm.ActivePalette.PrimaryAccent.Should().Be(ColorPalette.EmeraldSynth.PrimaryAccent);
        notifiedPalette.Should().Be(ColorPalette.EmeraldSynth);
    }
}
