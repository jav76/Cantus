namespace Cantus.Core.Models;

public enum LyricsOverrideKind
{
    /// <summary>The owner picked a specific provider candidate.</summary>
    Candidate = 0,

    /// <summary>The owner marked the track as having no lyrics.</summary>
    NoLyrics = 1
}

/// <summary>
/// The owner's correction for a track's lyrics. It applies server-wide and
/// takes precedence over the automatic match and the negative cache until it
/// is cleared.
/// </summary>
/// <param name="TrackId">Spotify track id the override applies to.</param>
/// <param name="Kind">What the owner chose.</param>
/// <param name="Provider">Source of the chosen candidate; null for <see cref="LyricsOverrideKind.NoLyrics"/>.</param>
/// <param name="ProviderCandidateId">The provider's id for the chosen candidate; null for <see cref="LyricsOverrideKind.NoLyrics"/>.</param>
/// <param name="Lyrics">The chosen lyrics; null for <see cref="LyricsOverrideKind.NoLyrics"/>.</param>
public sealed record LyricsOverride(
    string TrackId,
    LyricsOverrideKind Kind,
    string? Provider,
    string? ProviderCandidateId,
    SyncedLyrics? Lyrics)
{
    public static LyricsOverride ForCandidate(string provider, string providerCandidateId, SyncedLyrics lyrics) =>
        new(lyrics.TrackId, LyricsOverrideKind.Candidate, provider, providerCandidateId, lyrics);

    public static LyricsOverride ForNoLyrics(string trackId) =>
        new(trackId, LyricsOverrideKind.NoLyrics, null, null, null);
}
