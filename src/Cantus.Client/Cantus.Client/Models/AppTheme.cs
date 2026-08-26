using Windows.UI;

namespace Cantus.Client.Models;

public enum ThemeMode
{
    Dynamic,
    MidnightViolet,
    EmeraldSynth,
    CyberpunkSunset,
    NordicSlate,
    OLEDMonochrome,
    SolarizedDark
}

public sealed record ColorPalette(
    string Name,
    Color Background,
    Color SurfaceCard,
    Color CardBorder,
    Color PrimaryAccent,
    Color SecondaryAccent,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color GlowColor,
    Color ActiveLyricColor,
    Color PastLyricColor,
    Color UpcomingLyricColor)
{
    public static ColorPalette MidnightViolet { get; } = new(
        Name: "Midnight Violet",
        Background: Color.FromArgb(255, 12, 13, 20),           // #0C0D14
        SurfaceCard: Color.FromArgb(204, 18, 20, 32),         // #CC121420
        CardBorder: Color.FromArgb(34, 255, 255, 255),        // #22FFFFFF
        PrimaryAccent: Color.FromArgb(255, 139, 92, 246),      // #8B5CF6
        SecondaryAccent: Color.FromArgb(255, 192, 132, 252),  // #C084FC
        TextPrimary: Color.FromArgb(255, 248, 250, 252),      // #F8FAFC
        TextSecondary: Color.FromArgb(255, 203, 213, 225),    // #CBD5E1
        TextMuted: Color.FromArgb(255, 100, 116, 139),        // #64748B
        GlowColor: Color.FromArgb(60, 139, 92, 246),          // Soft purple glow
        ActiveLyricColor: Color.FromArgb(255, 255, 255, 255), // Pure white
        PastLyricColor: Color.FromArgb(120, 100, 116, 139),   // Dimmed slate
        UpcomingLyricColor: Color.FromArgb(200, 148, 163, 184) // Soft slate
    );

    public static ColorPalette EmeraldSynth { get; } = new(
        Name: "Emerald Synth",
        Background: Color.FromArgb(255, 9, 20, 16),           // #091410
        SurfaceCard: Color.FromArgb(204, 14, 28, 22),         // #CC0E1C16
        CardBorder: Color.FromArgb(34, 16, 185, 129),        // #2210B981
        PrimaryAccent: Color.FromArgb(255, 16, 185, 129),      // #10B981
        SecondaryAccent: Color.FromArgb(255, 52, 211, 153),   // #34D399
        TextPrimary: Color.FromArgb(255, 240, 253, 244),      // #F0FDF4
        TextSecondary: Color.FromArgb(255, 167, 243, 208),    // #A7F3D0
        TextMuted: Color.FromArgb(255, 74, 110, 93),          // Muted forest
        GlowColor: Color.FromArgb(60, 16, 185, 129),          // Soft emerald glow
        ActiveLyricColor: Color.FromArgb(255, 255, 255, 255),
        PastLyricColor: Color.FromArgb(120, 74, 110, 93),
        UpcomingLyricColor: Color.FromArgb(200, 167, 243, 208)
    );

    public static ColorPalette CyberpunkSunset { get; } = new(
        Name: "Cyberpunk Sunset",
        Background: Color.FromArgb(255, 15, 11, 25),          // #0F0B19
        SurfaceCard: Color.FromArgb(204, 26, 17, 40),         // #CC1A1128
        CardBorder: Color.FromArgb(34, 244, 63, 94),          // #22F43F5E
        PrimaryAccent: Color.FromArgb(255, 244, 63, 94),      // #F43F5E (Neon Rose)
        SecondaryAccent: Color.FromArgb(255, 245, 158, 11),   // #F59E0B (Amber Glow)
        TextPrimary: Color.FromArgb(255, 255, 241, 242),      // #FFF1F2
        TextSecondary: Color.FromArgb(255, 254, 205, 211),    // #FECDD3
        TextMuted: Color.FromArgb(255, 136, 91, 110),
        GlowColor: Color.FromArgb(60, 244, 63, 94),
        ActiveLyricColor: Color.FromArgb(255, 255, 255, 255),
        PastLyricColor: Color.FromArgb(120, 136, 91, 110),
        UpcomingLyricColor: Color.FromArgb(200, 254, 205, 211)
    );

    public static ColorPalette NordicSlate { get; } = new(
        Name: "Nordic Slate",
        Background: Color.FromArgb(255, 15, 23, 42),          // #0F172A
        SurfaceCard: Color.FromArgb(204, 30, 41, 59),         // #CC1E293B
        CardBorder: Color.FromArgb(34, 6, 182, 212),          // #2206B6D4
        PrimaryAccent: Color.FromArgb(255, 6, 182, 212),       // #06B6D4 (Cyan)
        SecondaryAccent: Color.FromArgb(255, 56, 189, 248),   // #38BDF8 (Sky)
        TextPrimary: Color.FromArgb(255, 241, 245, 249),      // #F1F5F9
        TextSecondary: Color.FromArgb(255, 203, 213, 225),    // #CBD5E1
        TextMuted: Color.FromArgb(255, 100, 116, 139),
        GlowColor: Color.FromArgb(60, 6, 182, 212),
        ActiveLyricColor: Color.FromArgb(255, 255, 255, 255),
        PastLyricColor: Color.FromArgb(120, 100, 116, 139),
        UpcomingLyricColor: Color.FromArgb(200, 203, 213, 225)
    );

    public static ColorPalette OLEDMonochrome { get; } = new(
        Name: "OLED Monochrome",
        Background: Color.FromArgb(255, 0, 0, 0),              // #000000 True Black
        SurfaceCard: Color.FromArgb(220, 15, 15, 15),         // #DC0F0F0F
        CardBorder: Color.FromArgb(50, 255, 255, 255),        // #32FFFFFF
        PrimaryAccent: Color.FromArgb(255, 255, 255, 255),     // High contrast white
        SecondaryAccent: Color.FromArgb(255, 200, 200, 200),
        TextPrimary: Color.FromArgb(255, 255, 255, 255),
        TextSecondary: Color.FromArgb(255, 220, 220, 220),
        TextMuted: Color.FromArgb(255, 120, 120, 120),
        GlowColor: Color.FromArgb(40, 255, 255, 255),
        ActiveLyricColor: Color.FromArgb(255, 255, 255, 255),
        PastLyricColor: Color.FromArgb(100, 120, 120, 120),
        UpcomingLyricColor: Color.FromArgb(180, 200, 200, 200)
    );

    public static ColorPalette SolarizedDark { get; } = new(
        Name: "Solarized Dark",
        Background: Color.FromArgb(255, 0, 43, 54),           // #002B36 (base03)
        SurfaceCard: Color.FromArgb(204, 7, 54, 66),          // #CC073642 (base02)
        CardBorder: Color.FromArgb(34, 38, 139, 210),         // #22268BD2 (Solarized Blue)
        PrimaryAccent: Color.FromArgb(255, 38, 139, 210),      // #268BD2 (Solarized Blue)
        SecondaryAccent: Color.FromArgb(255, 42, 161, 152),   // #2AA198 (Solarized Cyan)
        TextPrimary: Color.FromArgb(255, 253, 246, 227),      // #FDF6E3 (base3)
        TextSecondary: Color.FromArgb(255, 147, 161, 161),    // #93A1A1 (base1)
        TextMuted: Color.FromArgb(255, 101, 123, 131),        // #657B83 (base00)
        GlowColor: Color.FromArgb(60, 38, 139, 210),          // Soft blue glow
        ActiveLyricColor: Color.FromArgb(255, 253, 246, 227), // #FDF6E3
        PastLyricColor: Color.FromArgb(120, 88, 110, 117),    // #586E75 (base01)
        UpcomingLyricColor: Color.FromArgb(200, 147, 161, 161) // #93A1A1 (base1)
    );

    public static ColorPalette GetPredefined(ThemeMode mode) => mode switch
    {
        ThemeMode.EmeraldSynth => EmeraldSynth,
        ThemeMode.CyberpunkSunset => CyberpunkSunset,
        ThemeMode.NordicSlate => NordicSlate,
        ThemeMode.OLEDMonochrome => OLEDMonochrome,
        ThemeMode.SolarizedDark => SolarizedDark,
        _ => MidnightViolet
    };
}
