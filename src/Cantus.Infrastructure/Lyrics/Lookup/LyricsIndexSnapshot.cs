using Cantus.Core.Search;

namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>One searchable track of a <see cref="LyricsIndexSnapshot"/>.</summary>
/// <param name="TrackId">Spotify track id.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist names.</param>
/// <param name="Album">Album name, when known.</param>
/// <param name="Document">Token ids against the snapshot's term table, line starts and original lines.</param>
/// <param name="ExpiresAtUtc">When the underlying cache row expires, or null.</param>
public sealed record IndexedSong(
    string TrackId,
    string Title,
    string Artist,
    string? Album,
    LyricDocument Document,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// An immutable view of the effective lyrics: the songs and the shared vocabulary
/// with document frequencies. A refresh publishes a new snapshot and never changes
/// this one, so a scan in progress keeps a consistent view.
/// </summary>
public sealed class LyricsIndexSnapshot
{
    public LyricsIndexSnapshot(IReadOnlyList<IndexedSong> songs, LyricTermTable terms, DateTimeOffset builtAtUtc)
    {
        Songs = songs;
        Terms = terms;
        BuiltAtUtc = builtAtUtc;
    }

    public IReadOnlyList<IndexedSong> Songs { get; }

    public LyricTermTable Terms { get; }

    /// <summary>When the last full rebuild ran; incremental refreshes keep this value.</summary>
    public DateTimeOffset BuiltAtUtc { get; }

    public int Count => Songs.Count;
}

/// <summary>The local candidates of one scan over a snapshot.</summary>
/// <param name="Candidates">Songs scoring at least <see cref="LyricMatchConstants.RESULT_MIN_SCORE"/>.</param>
/// <param name="CorpusSize">Songs in the snapshot.</param>
/// <param name="SongsScanned">Songs scored before the scan finished or ran out of time.</param>
/// <param name="Truncated">True when the time budget stopped the scan; the outcome must not be accepted.</param>
/// <param name="Elapsed">Time spent scoring.</param>
public sealed record LyricsScanResult(
    IReadOnlyList<SongCandidate> Candidates,
    int CorpusSize,
    int SongsScanned,
    bool Truncated,
    TimeSpan Elapsed);
