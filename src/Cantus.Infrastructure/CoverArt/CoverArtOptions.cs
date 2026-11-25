namespace Cantus.Infrastructure.CoverArt;

/// <summary>
/// Options for looking up a cover in the open catalogue (MusicBrainz for the match, the Cover
/// Art Archive for the image) when a Local player offers none.
/// </summary>
public sealed class CoverArtOptions
{
    public const string SECTION_NAME = "CoverArt";

    /// <summary>
    /// Turning this off stops every catalogue request, and tracks whose player offers no cover
    /// are shown without one again. Cached answers are kept for when it is turned back on.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string MusicBrainzBaseUrl { get; set; } = "https://musicbrainz.org";

    public string CoverArtArchiveBaseUrl { get; set; } = "https://coverartarchive.org";

    /// <summary>
    /// MusicBrainz requires an application name, a version and a way to contact the maintainers,
    /// and throttles clients that send a generic value.
    /// </summary>
    public string UserAgent { get; set; } = "CantusSyncedLyrics/1.0.0 ( https://github.com/jav76/Cantus )";

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Days a found cover is kept before the catalogue is asked again.</summary>
    public int CacheDays { get; set; } = 90;

    /// <summary>Days a "no cover" answer is kept before the catalogue is asked again.</summary>
    public int NegativeCacheDays { get; set; } = 30;
}
