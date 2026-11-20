using System.ComponentModel.DataAnnotations;

namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// Options for the song lookup by remembered lyrics, including the share of
/// each lyrics provider's request rate that lookups may spend.
/// </summary>
public sealed class SongLookupOptions
{
    public const string SECTION_NAME = "SongLookup";

    public const int MAX_PROVIDER_REQUESTS_PER_MINUTE = 60;

    /// <summary>
    /// Lookup requests sent to LRCLIB per rolling minute. Now-playing fetches
    /// are not counted and are never refused by this budget.
    /// </summary>
    [Range(1, MAX_PROVIDER_REQUESTS_PER_MINUTE)]
    public int LrclibRequestsPerMinute { get; set; } = 10;

    /// <summary>
    /// Lookup requests sent to NetEase per rolling minute. Now-playing fetches
    /// are not counted and are never refused by this budget.
    /// </summary>
    [Range(1, MAX_PROVIDER_REQUESTS_PER_MINUTE)]
    public int NeteaseRequestsPerMinute { get; set; } = 6;
}
