namespace Cantus.Client.Models;

/// <summary>
/// Defines the active screen form factor breakpoint for adaptive UI layout rendering.
/// </summary>
public enum LayoutBreakpoint
{
    /// <summary>
    /// Small screens (< 680px): Mobile smartphones, compact vertical panes.
    /// Single-column layout with compact header, collapsible Now Playing card, and touch-focused lyrics stage.
    /// </summary>
    Small,

    /// <summary>
    /// Medium screens (680px - 1079px): Tablets, foldable devices, split-screen desktop windows.
    /// Adaptive two-column layout with medium album art and condensed telemetry.
    /// </summary>
    Medium,

    /// <summary>
    /// Large screens (1080px - 1919px): Desktop monitors, full-screen laptops, wide web browsers.
    /// Expansive dual-column layout with full telemetry pill bar, generous 332px track card, and high-fidelity lyrics stage.
    /// </summary>
    Large,

    /// <summary>
    /// Fullscreen / TV screens (>= 1920px or Fullscreen Kiosk mode): 10-foot living room experience, 4K TVs, ultrawide presentation.
    /// High-contrast large center-aligned lyrics with floating minimal bottom telemetry bar.
    /// </summary>
    FullscreenTv
}

/// <summary>
/// Screen orientation mode.
/// </summary>
public enum LayoutOrientation
{
    Portrait,
    Landscape
}

/// <summary>
/// Header bar display density.
/// </summary>
public enum HeaderDisplayMode
{
    Full,
    Compact,
    Minimal,
    Hidden
}

/// <summary>
/// Active view mode for small / mobile screen tab switching.
/// </summary>
public enum MobileViewMode
{
    Lyrics,
    NowPlaying,
    SyncAndSettings
}
