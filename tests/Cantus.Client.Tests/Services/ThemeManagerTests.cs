using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using FluentAssertions;
using Windows.UI;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ThemeManagerTests
{
    private const string ALBUM_ART_URL = "https://i.scdn.co/image/ab67616d0000b2738863bc11d2aa12b54f5aeb36";
    private const string OTHER_ALBUM_ART_URL = "https://i.scdn.co/image/other-album-art";

    // 8x8 solid red PNG
    private const string RED_PNG_BASE64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
        "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAWSURBVChTY/jPwPAfH2ZAF0DHw0MBAMLXf4F1" +
        "N6FEAAAAAElFTkSuQmCC";

    // 8x8 PNG: top 6 rows blue (48 px), bottom 2 rows yellow (16 px)
    private const string BLUE_YELLOW_PNG_BASE64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
        "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAmSURBVChTY2Bg+P8fP8YQQMcYAugYQwAdYwig" +
        "YwwBNAxGeDBBBQB5PY9xfgNXXQAAAABJRU5ErkJggg==";

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseHandler { get; set; }

        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? AsyncResponseHandler { get; set; }

        public int InvocationCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;

            if (AsyncResponseHandler is not null)
            {
                return await AsyncResponseHandler(request);
            }

            if (ResponseHandler is not null)
            {
                return ResponseHandler(request);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage CreateImageResponse(string base64)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Convert.FromBase64String(base64))
        };
    }

    private static ThemeManager CreateThemeManager(MockHttpMessageHandler handler)
    {
        return new ThemeManager(new AlbumArtColorService(new HttpClient(handler)));
    }

    private static async Task AwaitExtractionAsync(ThemeManager tm)
    {
        Task? extractionTask = tm.ActiveExtractionTask;
        if (extractionTask is not null)
        {
            await extractionTask;
        }
    }

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
    public async Task DynamicTheme_WithTrackMetadata_GeneratesHarmoniousPalette()
    {
        // Arrange - artwork fetch fails, so the metadata fallback palette applies
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Act
        tm.UpdateTrackMetadata(
            title: "Blinding Lights",
            artist: "The Weeknd",
            albumArtUrl: ALBUM_ART_URL);
        await AwaitExtractionAsync(tm);

        // Assert
        tm.ActivePalette.Name.Should().Contain("Blinding Lights");
        tm.ActivePalette.Background.A.Should().Be(ColorExtractionHelper.OPAQUE_ALPHA);
        tm.ActivePalette.PrimaryAccent.A.Should().Be(ColorExtractionHelper.OPAQUE_ALPHA);
        tm.ActivePalette.GlowColor.A.Should().Be(ColorExtractionHelper.GLOW_COLOR_ALPHA);
    }

    [Fact]
    public async Task DynamicTheme_ExtractionSuccess_AppliesArtworkPalette()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Act
        tm.UpdateTrackMetadata(title: "Red Album", artist: "Artist", albumArtUrl: ALBUM_ART_URL);
        await AwaitExtractionAsync(tm);

        // Assert - accent derived from the red artwork
        tm.ActivePalette.Name.Should().Contain("Red Album");
        tm.ActivePalette.PrimaryAccent.R.Should().BeGreaterThan(150);
        tm.ActivePalette.PrimaryAccent.R.Should().BeGreaterThan(tm.ActivePalette.PrimaryAccent.G);
        tm.ActivePalette.PrimaryAccent.R.Should().BeGreaterThan(tm.ActivePalette.PrimaryAccent.B);

        // Background stays dark and hue-matched
        (float _, float _, float backgroundLightness) = ColorExtractionHelper.RgbToHsl(tm.ActivePalette.Background);
        backgroundLightness.Should().BeLessThan(0.1f);

        // Alpha conventions preserved
        tm.ActivePalette.SurfaceCard.A.Should().Be(ColorExtractionHelper.SURFACE_CARD_ALPHA);
        tm.ActivePalette.CardBorder.A.Should().Be(ColorExtractionHelper.CARD_BORDER_ALPHA);
        tm.ActivePalette.GlowColor.A.Should().Be(ColorExtractionHelper.GLOW_COLOR_ALPHA);

        // Artwork URL exposed for the ambient backdrop alongside the palette
        tm.AmbientArtworkUrl.Should().Be(ALBUM_ART_URL);
    }

    [Fact]
    public async Task DynamicTheme_ExtractionFailure_KeepsMetadataFallbackPalette()
    {
        // Arrange
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Act
        tm.UpdateTrackMetadata(title: "Some Track", artist: "Some Artist", albumArtUrl: ALBUM_ART_URL);
        ColorPalette fallbackPalette = tm.ActivePalette;
        await AwaitExtractionAsync(tm);

        // Assert - palette is byte-for-byte the metadata fallback, with no ambient backdrop
        tm.ActivePalette.Should().Be(fallbackPalette);
        tm.ActivePalette.Should().Be(
            ColorExtractionHelper.GeneratePaletteFromMetadata("Some Track", "Some Artist", ALBUM_ART_URL));
        tm.AmbientArtworkUrl.Should().BeNull();
    }

    [Fact]
    public async Task DynamicTheme_StaleExtraction_IsDiscarded()
    {
        // Arrange - the first track's artwork fetch blocks until released
        TaskCompletionSource<bool> firstFetchGate = new();
        MockHttpMessageHandler handler = new()
        {
            AsyncResponseHandler = async request =>
            {
                if (request.RequestUri!.ToString() == ALBUM_ART_URL)
                {
                    await firstFetchGate.Task;
                    return CreateImageResponse(RED_PNG_BASE64);
                }

                return CreateImageResponse(BLUE_YELLOW_PNG_BASE64);
            }
        };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Act - track changes while the first extraction is still in flight
        tm.UpdateTrackMetadata(title: "First Track", artist: "Artist", albumArtUrl: ALBUM_ART_URL);
        Task? firstExtraction = tm.ActiveExtractionTask;
        tm.UpdateTrackMetadata(title: "Second Track", artist: "Artist", albumArtUrl: OTHER_ALBUM_ART_URL);
        await AwaitExtractionAsync(tm);
        ColorPalette secondTrackPalette = tm.ActivePalette;

        firstFetchGate.SetResult(true);
        if (firstExtraction is not null)
        {
            await firstExtraction;
        }

        // Assert - the late first-track result must not overwrite the current palette
        tm.ActivePalette.Should().Be(secondTrackPalette);
        tm.ActivePalette.Name.Should().Contain("Second Track");
    }

    [Fact]
    public async Task DynamicTheme_SameAlbumArtTwice_FetchesOnlyOnce()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Act - repeated playback polls for the same track
        tm.UpdateTrackMetadata(title: "Track", artist: "Artist", albumArtUrl: ALBUM_ART_URL);
        await AwaitExtractionAsync(tm);
        ColorPalette extractedPalette = tm.ActivePalette;
        tm.UpdateTrackMetadata(title: "Track", artist: "Artist", albumArtUrl: ALBUM_ART_URL);
        await AwaitExtractionAsync(tm);

        // Assert
        handler.InvocationCount.Should().Be(1);
        tm.ActivePalette.Should().Be(extractedPalette);
    }

    [Fact]
    public void NonDynamicMode_UpdateTrackMetadata_DoesNotFetchArtwork()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.MidnightViolet);

        // Act
        tm.UpdateTrackMetadata(title: "Track", artist: "Artist", albumArtUrl: ALBUM_ART_URL);

        // Assert
        handler.InvocationCount.Should().Be(0);
        tm.ActiveExtractionTask.Should().BeNull();
        tm.ActivePalette.Should().Be(ColorPalette.MidnightViolet);
    }

    [Fact]
    public async Task DynamicTheme_PaletteChanged_FiresForFallbackThenArtwork()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);

        int paletteChangedCount = 0;
        tm.PaletteChanged += _ => paletteChangedCount++;

        // Act
        tm.UpdateTrackMetadata(title: "Track", artist: "Artist", albumArtUrl: ALBUM_ART_URL);
        await AwaitExtractionAsync(tm);

        // Assert - once for the instant fallback, once for the artwork palette
        paletteChangedCount.Should().Be(2);
    }

    [Fact]
    public async Task DynamicTheme_TrackWithoutAlbumArt_CancelsPendingExtraction()
    {
        // Arrange - extraction for the first track never completes on its own
        TaskCompletionSource<bool> fetchGate = new();
        MockHttpMessageHandler handler = new()
        {
            AsyncResponseHandler = async _ =>
            {
                await fetchGate.Task;
                return CreateImageResponse(RED_PNG_BASE64);
            }
        };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);
        tm.UpdateTrackMetadata(title: "Art Track", artist: "Artist", albumArtUrl: ALBUM_ART_URL);
        Task? pendingExtraction = tm.ActiveExtractionTask;

        // Act - next track has no artwork
        tm.UpdateTrackMetadata(title: "No Art Track", artist: "Artist", albumArtUrl: null);
        fetchGate.SetResult(true);
        if (pendingExtraction is not null)
        {
            await pendingExtraction;
        }

        // Assert
        tm.ActivePalette.Name.Should().Contain("No Art Track");
        tm.ActivePalette.Should().Be(
            ColorExtractionHelper.GeneratePaletteFromMetadata("No Art Track", "Artist", null));
    }

    [Fact]
    public async Task DynamicTheme_CachedArtwork_AppliesImmediatelyOnModeReentry()
    {
        // Arrange - populate the cache with one extraction
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        ThemeManager tm = CreateThemeManager(handler);
        tm.SetThemeMode(ThemeMode.Dynamic);
        tm.UpdateTrackMetadata(title: "Track", artist: "Artist", albumArtUrl: ALBUM_ART_URL);
        await AwaitExtractionAsync(tm);
        ColorPalette artworkPalette = tm.ActivePalette;

        // Act - leave Dynamic mode and come back
        tm.SetThemeMode(ThemeMode.MidnightViolet);
        string? ambientWhilePreset = tm.AmbientArtworkUrl;
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Assert - artwork palette and ambient backdrop restored synchronously from cache, no second fetch
        ambientWhilePreset.Should().BeNull();
        tm.ActivePalette.Should().Be(artworkPalette);
        tm.AmbientArtworkUrl.Should().Be(ALBUM_ART_URL);
        handler.InvocationCount.Should().Be(1);
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

    [Theory]
    [InlineData(ThemeMode.Dynamic, "Dynamic Palette")]
    [InlineData(ThemeMode.MidnightViolet, "Midnight Violet")]
    [InlineData(ThemeMode.EmeraldSynth, "Emerald Synth")]
    [InlineData(ThemeMode.CyberpunkSunset, "Cyberpunk Sunset")]
    [InlineData(ThemeMode.NordicSlate, "Nordic Slate")]
    [InlineData(ThemeMode.OLEDMonochrome, "OLED Monochrome")]
    [InlineData(ThemeMode.SolarizedDark, "Solarized Dark")]
    public void GetDisplayName_ReturnsHumanReadableModeName(ThemeMode mode, string expectedName)
    {
        // Act & Assert
        mode.GetDisplayName().Should().Be(expectedName);
    }

    [Fact]
    public void RgbToHsl_RoundTripsWithHslToRgb()
    {
        // Arrange
        Color[] colors =
        {
            Color.FromArgb(255, 255, 0, 0),
            Color.FromArgb(255, 30, 144, 255),
            Color.FromArgb(255, 128, 128, 128),
            Color.FromArgb(255, 46, 204, 113)
        };

        foreach (Color original in colors)
        {
            // Act
            (float hue, float saturation, float lightness) = ColorExtractionHelper.RgbToHsl(original);
            Color roundTripped = ColorExtractionHelper.HslToRgb(hue, saturation, lightness);

            // Assert
            ((int)roundTripped.R).Should().BeCloseTo(original.R, 2);
            ((int)roundTripped.G).Should().BeCloseTo(original.G, 2);
            ((int)roundTripped.B).Should().BeCloseTo(original.B, 2);
        }
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

    [Fact]
    public void PaletteChange_ExposesAndNotifiesTheActiveColors()
    {
        // Arrange
        ThemeManager tm = new();
        tm.SetThemeMode(ThemeMode.MidnightViolet);
        List<string?> notified = new();
        tm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        tm.SetThemeMode(ThemeMode.EmeraldSynth);

        // Assert
        tm.ActivePrimaryAccentColor.Should().Be(ColorPalette.EmeraldSynth.PrimaryAccent);
        tm.ActiveBackgroundColor.Should().Be(ColorPalette.EmeraldSynth.Background);
        notified.Should().Contain(nameof(ThemeManager.ActivePrimaryAccentColor));
        notified.Should().Contain(nameof(ThemeManager.ActiveBackgroundColor));
    }

    [Fact]
    public void OnPrimaryAccent_OledMonochromeWhiteAccent_IsNearBlack()
    {
        // Arrange
        ThemeManager tm = new();

        // Act
        tm.SetThemeMode(ThemeMode.OLEDMonochrome);

        // Assert
        ThemeManager.GetRelativeLuminance(tm.ActiveOnPrimaryAccentColor).Should().BeLessThan(0.05);
        tm.ActiveOnPrimaryAccentColor.A.Should().Be(255);
    }

    [Theory]
    [InlineData(ThemeMode.MidnightViolet)]
    [InlineData(ThemeMode.EmeraldSynth)]
    [InlineData(ThemeMode.CyberpunkSunset)]
    [InlineData(ThemeMode.NordicSlate)]
    [InlineData(ThemeMode.SolarizedDark)]
    public void OnPrimaryAccent_ColouredAccents_StayWhite(ThemeMode mode)
    {
        // Arrange
        ThemeManager tm = new();

        // Act
        tm.SetThemeMode(mode);

        // Assert
        tm.ActiveOnPrimaryAccentColor.Should().Be(Color.FromArgb(255, 255, 255, 255));
    }

    [Fact]
    public void OnPrimaryAccent_Dynamic_FollowsTheExtractedAccent()
    {
        // Arrange
        MockHttpMessageHandler handler = new();
        ThemeManager tm = CreateThemeManager(handler);
        tm.UpdateTrackMetadata("Song", "Artist", null);

        // Act
        tm.SetThemeMode(ThemeMode.Dynamic);

        // Assert
        tm.ActiveOnPrimaryAccentColor.Should().Be(ThemeManager.GetOnAccentColor(tm.ActivePalette.PrimaryAccent));
    }

    [Fact]
    public void GetOnAccentColor_PicksDarkForLightAccentsAndWhiteForDarkOnes()
    {
        // Act
        Color onYellow = ThemeManager.GetOnAccentColor(Color.FromArgb(255, 250, 204, 21));
        Color onBlue = ThemeManager.GetOnAccentColor(Color.FromArgb(255, 37, 99, 235));

        // Assert
        ThemeManager.GetRelativeLuminance(onYellow).Should().BeLessThan(0.05);
        onBlue.Should().Be(Color.FromArgb(255, 255, 255, 255));
    }

    [Fact]
    public void PaletteChange_NotifiesOnPrimaryAccentAndMenuSurfaceBrushes()
    {
        // Arrange
        ThemeManager tm = new();
        List<string?> notified = new();
        tm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        tm.SetThemeMode(ThemeMode.OLEDMonochrome);

        // Assert
        notified.Should().Contain(nameof(ThemeManager.OnPrimaryAccentBrush));
        notified.Should().Contain(nameof(ThemeManager.MenuSurfaceBrush));
        notified.Should().Contain(nameof(ThemeManager.ActiveOnPrimaryAccentColor));
    }

    [Fact]
    public void GetMenuSurfaceColor_IsTheOpaqueCardColour()
    {
        // Act
        Color surface = ThemeManager.GetMenuSurfaceColor(ColorPalette.NordicSlate);

        // Assert
        surface.Should().Be(Color.FromArgb(255, 30, 41, 59));
    }

    [Theory]
    [InlineData("CantusTextPrimaryBrush", ThemeManager.ThemeBrush.TextPrimary)]
    [InlineData("CantusOnPrimaryAccentBrush", ThemeManager.ThemeBrush.OnPrimaryAccent)]
    [InlineData("OutlinedTextBoxForegroundFocused", ThemeManager.ThemeBrush.TextPrimary)]
    [InlineData("OutlinedTextBoxPlaceholderForeground", ThemeManager.ThemeBrush.TextMuted)]
    [InlineData("OnSurfaceBrush", ThemeManager.ThemeBrush.TextPrimary)]
    [InlineData("SurfaceBrush", ThemeManager.ThemeBrush.MenuSurface)]
    [InlineData("ComboBoxDropDownBackground", ThemeManager.ThemeBrush.MenuSurface)]
    [InlineData("CheckBoxGlyphForegroundChecked", ThemeManager.ThemeBrush.OnPrimaryAccent)]
    [InlineData("ProgressRingForeground", ThemeManager.ThemeBrush.PrimaryAccent)]
    public void ResourceBrushKeys_MapControlKeysToThemeBrushes(string key, ThemeManager.ThemeBrush expected)
    {
        // Assert
        ThemeManager.ResourceBrushKeys.Should().ContainKey(key).WhoseValue.Should().Be(expected);
    }

    [Fact]
    public void ResourceBrushKeys_LeaveButtonTemplatesAlone()
    {
        // Assert
        ThemeManager.ResourceBrushKeys.Keys.Should().NotContain(key => key.Contains("Button") && !key.StartsWith("TextBoxDeleteButton"));
    }
}
