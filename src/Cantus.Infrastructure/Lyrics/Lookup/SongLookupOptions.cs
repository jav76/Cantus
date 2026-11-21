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
    public const int MAX_ONLINE_LOOKUPS_PER_MINUTE = 60;

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

    /// <summary>
    /// Lookups per Spotify user per rolling minute that may call a lyrics
    /// provider. Local searches and query-cache hits do not count.
    /// </summary>
    [Range(1, MAX_ONLINE_LOOKUPS_PER_MINUTE)]
    public int OnlineLookupsPerMinute { get; set; } = 6;

    /// <summary>Master switch for calling lyrics providers from a lookup.</summary>
    public bool ExternalSearchEnabled { get; set; } = true;

    /// <summary>Switches the LRCLIB title-guess search on or off; NetEase is unaffected.</summary>
    public bool LrclibTitleGuessEnabled { get; set; } = true;
}
