using Cantus.Core.Models;

namespace Cantus.Infrastructure.Lyrics;

public enum LyricsOverrideStatus
{
    Applied = 0,

    /// <summary>No enabled provider has the requested name.</summary>
    UnknownProvider = 1,

    /// <summary>The provider answered but has no usable lyrics under that candidate id.</summary>
    CandidateNotFound = 2,

    /// <summary>The provider could not be reached; nothing was changed.</summary>
    ProviderUnavailable = 3
}

/// <param name="Status">Whether the override was stored, and why not otherwise.</param>
/// <param name="Lyrics">The lyrics now in effect for the track when <see cref="Status"/> is Applied.</param>
public sealed record LyricsOverrideOutcome(LyricsOverrideStatus Status, SyncedLyrics? Lyrics)
{
    public static LyricsOverrideOutcome Applied(SyncedLyrics? lyrics) => new(LyricsOverrideStatus.Applied, lyrics);

    public static LyricsOverrideOutcome Rejected(LyricsOverrideStatus status) => new(status, null);
}
