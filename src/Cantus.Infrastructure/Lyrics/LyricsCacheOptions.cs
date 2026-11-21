namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Options for the lyrics cache layer, independent of any single provider:
/// the negative-cache TTL applies to the whole fallback chain, not just
/// LRCLIB.
/// </summary>
public sealed class LyricsCacheOptions
{
    public const string SECTION_NAME = "LyricsCache";
    public int NegativeCacheDays { get; set; } = 30;

    /// <summary>
    /// How many times a cached entry without word timing is re-fetched to look for it. 0 disables upgrades.
    /// </summary>
    public int WordTimingUpgradeMaxAttempts { get; set; } = 3;

    /// <summary>Minimum days between word-timing upgrade attempts for the same track.</summary>
    public int WordTimingUpgradeIntervalDays { get; set; } = 7;
}
