using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Windows.UI;

namespace Cantus.Client.Models;

public static class ColorExtractionHelper
{
    private const float GRAYSCALE_SATURATION_THRESHOLD = 0.10f;
    private const float MIN_ACCENT_SATURATION = 0.35f;
    private const float MAX_ACCENT_SATURATION = 0.95f;
    private const float MIN_ACCENT_LIGHTNESS = 0.48f;
    private const float MAX_ACCENT_LIGHTNESS = 0.65f;
    private const float MIN_SECONDARY_HUE_SEPARATION = 30f;
    private const float SECONDARY_HUE_SHIFT = 35f;
    private const float MAX_BACKGROUND_SATURATION = 0.45f;

    private const float HUE_DEGREES_MAX = 360f;
    private const float HUE_DEGREES_HALF = 180f;
    private const float HUE_SECTOR_DEGREES = 60f;
    private const float COLOR_CHANNEL_MAX = 255f;

    private const int HASH_BYTE_SHIFT = 8;
    private const float METADATA_BASE_SATURATION = 0.70f;
    private const int METADATA_SATURATION_RANGE = 25;
    private const float METADATA_BASE_LIGHTNESS = 0.50f;
    private const int METADATA_LIGHTNESS_RANGE = 15;
    private const float PERCENT_DIVISOR = 100f;
    private const float METADATA_BACKGROUND_SATURATION = 0.35f;

    private const float SECONDARY_SATURATION_FACTOR = 0.9f;
    private const float SECONDARY_LIGHTNESS_BOOST = 0.15f;
    private const float MAX_LIGHTNESS = 1.0f;

    private const float SWATCH_SATURATION_WEIGHT = 3f;
    private const float TARGET_ACCENT_LIGHTNESS = 0.5f;
    private const float LIGHTNESS_PENALTY_FACTOR = 2f;

    private const float BACKGROUND_LIGHTNESS = 0.05f;
    internal const float SURFACE_CARD_SATURATION = 0.28f;
    internal const float SURFACE_CARD_LIGHTNESS = 0.09f;

    internal const byte OPAQUE_ALPHA = 255;
    internal const byte SURFACE_CARD_ALPHA = 204;
    internal const byte CARD_BORDER_ALPHA = 40;
    internal const byte GLOW_COLOR_ALPHA = 60;
    internal const byte PAST_LYRIC_ALPHA = 120;
    internal const byte UPCOMING_LYRIC_ALPHA = 200;

    private const byte TEXT_PRIMARY_R = 248;
    private const byte TEXT_PRIMARY_G = 250;
    private const byte TEXT_PRIMARY_B = 252;

    private const byte TEXT_SECONDARY_R = 203;
    private const byte TEXT_SECONDARY_G = 213;
    private const byte TEXT_SECONDARY_B = 225;

    private const byte TEXT_MUTED_R = 100;
    private const byte TEXT_MUTED_G = 116;
    private const byte TEXT_MUTED_B = 139;

    private const byte ACTIVE_LYRIC_R = 255;
    private const byte ACTIVE_LYRIC_G = 255;
    private const byte ACTIVE_LYRIC_B = 255;

    private const byte PAST_LYRIC_R = 100;
    private const byte PAST_LYRIC_G = 116;
    private const byte PAST_LYRIC_B = 139;

    private const byte UPCOMING_LYRIC_R = 148;
    private const byte UPCOMING_LYRIC_G = 163;
    private const byte UPCOMING_LYRIC_B = 184;

    public static ColorPalette GeneratePaletteFromMetadata(string? title, string? artist, string? albumArtUrl)
    {
        string seedString = $"{albumArtUrl ?? ""}|{artist ?? ""}|{title ?? ""}";
        if (string.IsNullOrWhiteSpace(seedString) || seedString == "||")
        {
            return ColorPalette.MidnightViolet;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedString));

        // Derive Hue (0-360), Saturation (0.6-0.9), Lightness (0.4-0.65) for primary accent
        float hue = (hash[0] | (hash[1] << HASH_BYTE_SHIFT)) % HUE_DEGREES_MAX;
        float sat = METADATA_BASE_SATURATION + (hash[2] % METADATA_SATURATION_RANGE) / PERCENT_DIVISOR;
        float lum = METADATA_BASE_LIGHTNESS + (hash[3] % METADATA_LIGHTNESS_RANGE) / PERCENT_DIVISOR;

        // Primary Accent Color
        Color primaryAccent = HslToRgb(hue, sat, lum);

        // Secondary Accent (complementary or analogous shifted by 35 degrees)
        float secondaryHue = (hue + SECONDARY_HUE_SHIFT) % HUE_DEGREES_MAX;
        Color secondaryAccent = HslToRgb(
            secondaryHue,
            sat * SECONDARY_SATURATION_FACTOR,
            Math.Min(MAX_LIGHTNESS, lum + SECONDARY_LIGHTNESS_BOOST));

        return BuildDynamicPalette(title, primaryAccent, secondaryAccent, hue, METADATA_BACKGROUND_SATURATION);
    }

    public static ColorPalette GeneratePaletteFromSwatches(string? title, IReadOnlyList<ColorSwatch> swatches)
    {
        if (swatches is null || swatches.Count == 0)
        {
            return ColorPalette.MidnightViolet;
        }

        int maxPopulation = 0;
        int dominantIndex = 0;
        for (int index = 0; index < swatches.Count; index++)
        {
            if (swatches[index].Population > maxPopulation)
            {
                maxPopulation = swatches[index].Population;
                dominantIndex = index;
            }
        }

        int primaryIndex = FindBestSwatch(swatches, maxPopulation, excludeIndex: -1, minHueSeparationFrom: null);
        ColorSwatch primarySwatch = swatches[primaryIndex];

        float primarySaturation = ClampAccentSaturation(primarySwatch.Saturation);
        float primaryLightness = Math.Clamp(primarySwatch.Lightness, MIN_ACCENT_LIGHTNESS, MAX_ACCENT_LIGHTNESS);
        Color primaryAccent = HslToRgb(primarySwatch.Hue, primarySaturation, primaryLightness);

        int secondaryIndex = FindBestSwatch(swatches, maxPopulation, excludeIndex: primaryIndex, minHueSeparationFrom: primarySwatch.Hue);
        Color secondaryAccent;
        if (secondaryIndex >= 0)
        {
            ColorSwatch secondarySwatch = swatches[secondaryIndex];
            float secondarySaturation = ClampAccentSaturation(secondarySwatch.Saturation) * SECONDARY_SATURATION_FACTOR;
            float secondaryLightness = Math.Min(
                MAX_LIGHTNESS,
                Math.Clamp(secondarySwatch.Lightness, MIN_ACCENT_LIGHTNESS, MAX_ACCENT_LIGHTNESS) + SECONDARY_LIGHTNESS_BOOST);
            secondaryAccent = HslToRgb(secondarySwatch.Hue, secondarySaturation, secondaryLightness);
        }
        else
        {
            float secondaryHue = (primarySwatch.Hue + SECONDARY_HUE_SHIFT) % HUE_DEGREES_MAX;
            secondaryAccent = HslToRgb(
                secondaryHue,
                primarySaturation * SECONDARY_SATURATION_FACTOR,
                Math.Min(MAX_LIGHTNESS, primaryLightness + SECONDARY_LIGHTNESS_BOOST));
        }

        ColorSwatch dominantSwatch = swatches[dominantIndex];
        float backgroundSaturation = Math.Min(dominantSwatch.Saturation, MAX_BACKGROUND_SATURATION);

        return BuildDynamicPalette(title, primaryAccent, secondaryAccent, dominantSwatch.Hue, backgroundSaturation);
    }

    public static Color HslToRgb(float h, float s, float l)
    {
        float r, g, b;

        if (s == 0)
        {
            r = g = b = l; // achromatic
        }
        else
        {
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            r = HueToRgb(p, q, h / HUE_DEGREES_MAX + 1f / 3f);
            g = HueToRgb(p, q, h / HUE_DEGREES_MAX);
            b = HueToRgb(p, q, h / HUE_DEGREES_MAX - 1f / 3f);
        }

        return Color.FromArgb(
            OPAQUE_ALPHA,
            (byte)Math.Clamp((int)Math.Round(r * COLOR_CHANNEL_MAX), 0, (int)COLOR_CHANNEL_MAX),
            (byte)Math.Clamp((int)Math.Round(g * COLOR_CHANNEL_MAX), 0, (int)COLOR_CHANNEL_MAX),
            (byte)Math.Clamp((int)Math.Round(b * COLOR_CHANNEL_MAX), 0, (int)COLOR_CHANNEL_MAX)
        );
    }

    public static (float Hue, float Saturation, float Lightness) RgbToHsl(Color color)
    {
        float r = color.R / COLOR_CHANNEL_MAX;
        float g = color.G / COLOR_CHANNEL_MAX;
        float b = color.B / COLOR_CHANNEL_MAX;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float lightness = (max + min) / 2f;

        float delta = max - min;
        if (delta <= 0f)
        {
            return (0f, 0f, lightness); // achromatic
        }

        float saturation = lightness > 0.5f
            ? delta / (2f - max - min)
            : delta / (max + min);

        float hue;
        if (r >= g && r >= b)
        {
            hue = (g - b) / delta + (g < b ? 6f : 0f);
        }
        else if (g >= b)
        {
            hue = (b - r) / delta + 2f;
        }
        else
        {
            hue = (r - g) / delta + 4f;
        }

        return (hue * HUE_SECTOR_DEGREES, saturation, lightness);
    }

    private static ColorPalette BuildDynamicPalette(
        string? title,
        Color primaryAccent,
        Color secondaryAccent,
        float backgroundHue,
        float backgroundSaturation)
    {
        // Dark Background (Hue matched, very low lightness)
        Color background = HslToRgb(backgroundHue, backgroundSaturation, BACKGROUND_LIGHTNESS);

        // Surface Card (Translucent 80%, slightly lighter)
        Color surfaceCardRgb = HslToRgb(backgroundHue, SURFACE_CARD_SATURATION, SURFACE_CARD_LIGHTNESS);
        Color surfaceCard = Color.FromArgb(SURFACE_CARD_ALPHA, surfaceCardRgb.R, surfaceCardRgb.G, surfaceCardRgb.B);

        // Subtle Card Border (Translucent 20% primary accent)
        Color cardBorder = Color.FromArgb(CARD_BORDER_ALPHA, primaryAccent.R, primaryAccent.G, primaryAccent.B);

        // Glow Color (25% opacity primary)
        Color glowColor = Color.FromArgb(GLOW_COLOR_ALPHA, primaryAccent.R, primaryAccent.G, primaryAccent.B);

        return new ColorPalette(
            Name: $"Dynamic ({title ?? "Track"})",
            Background: background,
            SurfaceCard: surfaceCard,
            CardBorder: cardBorder,
            PrimaryAccent: primaryAccent,
            SecondaryAccent: secondaryAccent,
            TextPrimary: Color.FromArgb(OPAQUE_ALPHA, TEXT_PRIMARY_R, TEXT_PRIMARY_G, TEXT_PRIMARY_B),
            TextSecondary: Color.FromArgb(OPAQUE_ALPHA, TEXT_SECONDARY_R, TEXT_SECONDARY_G, TEXT_SECONDARY_B),
            TextMuted: Color.FromArgb(OPAQUE_ALPHA, TEXT_MUTED_R, TEXT_MUTED_G, TEXT_MUTED_B),
            GlowColor: glowColor,
            ActiveLyricColor: Color.FromArgb(OPAQUE_ALPHA, ACTIVE_LYRIC_R, ACTIVE_LYRIC_G, ACTIVE_LYRIC_B),
            PastLyricColor: Color.FromArgb(PAST_LYRIC_ALPHA, PAST_LYRIC_R, PAST_LYRIC_G, PAST_LYRIC_B),
            UpcomingLyricColor: Color.FromArgb(UPCOMING_LYRIC_ALPHA, UPCOMING_LYRIC_R, UPCOMING_LYRIC_G, UPCOMING_LYRIC_B)
        );
    }

    private static int FindBestSwatch(
        IReadOnlyList<ColorSwatch> swatches,
        int maxPopulation,
        int excludeIndex,
        float? minHueSeparationFrom)
    {
        int bestIndex = -1;
        float bestScore = float.MinValue;

        for (int index = 0; index < swatches.Count; index++)
        {
            if (index == excludeIndex)
            {
                continue;
            }

            if (minHueSeparationFrom is float referenceHue
                && HueDistance(swatches[index].Hue, referenceHue) < MIN_SECONDARY_HUE_SEPARATION)
            {
                continue;
            }

            float score = ScoreSwatch(swatches[index], maxPopulation);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static float ScoreSwatch(ColorSwatch swatch, int maxPopulation)
    {
        float populationScore = maxPopulation > 0 ? (float)swatch.Population / maxPopulation : 0f;
        float lightnessScore = 1f - LIGHTNESS_PENALTY_FACTOR * Math.Abs(swatch.Lightness - TARGET_ACCENT_LIGHTNESS);
        return SWATCH_SATURATION_WEIGHT * swatch.Saturation + populationScore + lightnessScore;
    }

    private static float HueDistance(float hueA, float hueB)
    {
        float distance = Math.Abs(hueA - hueB) % HUE_DEGREES_MAX;
        return distance > HUE_DEGREES_HALF ? HUE_DEGREES_MAX - distance : distance;
    }

    private static float ClampAccentSaturation(float saturation)
    {
        // Genuinely grayscale artwork keeps its neutral character instead of
        // being forced into an artificially colorful accent.
        return saturation < GRAYSCALE_SATURATION_THRESHOLD
            ? saturation
            : Math.Clamp(saturation, MIN_ACCENT_SATURATION, MAX_ACCENT_SATURATION);
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }
}
