using Cantus.Client.Models;

namespace Cantus.Client.Services;

/// <summary>
/// Per-device lyrics view preferences. Reads return the persisted value or the
/// default; writes persist immediately.
/// </summary>
public interface ILyricsPreferences
{
    bool IsAutoScrollEnabled { get; set; }

    bool IsKaraokeModeEnabled { get; set; }

    /// <summary>Animated film grain over the whole app. Off by default.</summary>
    bool IsFilmGrainEnabled { get; set; }

    /// <summary>Soft darkening towards the window edges. Off by default.</summary>
    bool IsVignetteEnabled { get; set; }

    /// <summary>Faint horizontal CRT-style lines. Off by default.</summary>
    bool IsScanlinesEnabled { get; set; }

    /// <summary>Blurs the album art backdrop of the Dynamic theme. Off by default.</summary>
    bool IsBackgroundBlurEnabled { get; set; }

    /// <summary>Level bars over the played part of the seek bar. On by default.</summary>
    bool IsVuMeterEnabled { get; set; }

    /// <summary>The meter's level source, or null for the platform default.</summary>
    VuMeterSource? VuMeterSource { get; set; }

    /// <summary>Device latency compensation, clamped to the supported range on read and write.</summary>
    int LatencyCompensationMs { get; set; }
}
