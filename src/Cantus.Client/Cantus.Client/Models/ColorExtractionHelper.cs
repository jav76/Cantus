using System;
using System.Security.Cryptography;
using System.Text;
using Windows.UI;

namespace Cantus.Client.Models;

public static class ColorExtractionHelper
{
    public static ColorPalette GeneratePaletteFromMetadata(string? title, string? artist, string? albumArtUrl)
    {
        string seedString = $"{albumArtUrl ?? ""}|{artist ?? ""}|{title ?? ""}";
        if (string.IsNullOrWhiteSpace(seedString) || seedString == "||")
        {
            return ColorPalette.MidnightViolet;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedString));

        // Derive Hue (0-360), Saturation (0.6-0.9), Lightness (0.4-0.65) for primary accent
        float hue = (hash[0] | (hash[1] << 8)) % 360f;
        float sat = 0.70f + (hash[2] % 25) / 100f; // 0.70 - 0.95
        float lum = 0.50f + (hash[3] % 15) / 100f; // 0.50 - 0.65

        // Primary Accent Color
        Color primaryAccent = HslToRgb(hue, sat, lum);

        // Secondary Accent (complementary or analogous shifted by 35 degrees)
        float secondaryHue = (hue + 35f) % 360f;
        Color secondaryAccent = HslToRgb(secondaryHue, sat * 0.9f, Math.Min(1.0f, lum + 0.15f));

        // Dark Background (Hue matched, very low lightness 0.04 - 0.07, saturation 0.35)
        Color background = HslToRgb(hue, 0.35f, 0.05f);

        // Surface Card (Translucent 80%, slightly lighter 0.09)
        Color surfaceCardRgb = HslToRgb(hue, 0.28f, 0.09f);
        Color surfaceCard = Color.FromArgb(204, surfaceCardRgb.R, surfaceCardRgb.G, surfaceCardRgb.B);

        // Subtle Card Border (Translucent 20% primary accent)
        Color cardBorder = Color.FromArgb(40, primaryAccent.R, primaryAccent.G, primaryAccent.B);

        // Glow Color (25% opacity primary)
        Color glowColor = Color.FromArgb(60, primaryAccent.R, primaryAccent.G, primaryAccent.B);

        return new ColorPalette(
            Name: $"Dynamic ({title ?? "Track"})",
            Background: background,
            SurfaceCard: surfaceCard,
            CardBorder: cardBorder,
            PrimaryAccent: primaryAccent,
            SecondaryAccent: secondaryAccent,
            TextPrimary: Color.FromArgb(255, 248, 250, 252),
            TextSecondary: Color.FromArgb(255, 203, 213, 225),
            TextMuted: Color.FromArgb(255, 100, 116, 139),
            GlowColor: glowColor,
            ActiveLyricColor: Color.FromArgb(255, 255, 255, 255),
            PastLyricColor: Color.FromArgb(120, 100, 116, 139),
            UpcomingLyricColor: Color.FromArgb(200, 148, 163, 184)
        );
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
            r = HueToRgb(p, q, h / 360f + 1f / 3f);
            g = HueToRgb(p, q, h / 360f);
            b = HueToRgb(p, q, h / 360f - 1f / 3f);
        }

        return Color.FromArgb(
            255,
            (byte)Math.Clamp((int)Math.Round(r * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b * 255f), 0, 255)
        );
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
