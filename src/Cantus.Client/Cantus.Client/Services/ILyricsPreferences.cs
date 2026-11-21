namespace Cantus.Client.Services;

/// <summary>
/// Per-device lyrics view preferences. Reads return the persisted value or the
/// default; writes persist immediately.
/// </summary>
public interface ILyricsPreferences
{
    bool IsAutoScrollEnabled { get; set; }

    bool IsKaraokeModeEnabled { get; set; }

    /// <summary>Film grain and vignette over the whole app, in every theme. Off by default.</summary>
    bool IsPostProcessingEnabled { get; set; }

    /// <summary>Device latency compensation, clamped to the supported range on read and write.</summary>
    int LatencyCompensationMs { get; set; }
}
