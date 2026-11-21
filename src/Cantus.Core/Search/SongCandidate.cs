namespace Cantus.Core.Search;

public enum SongCandidateOrigin
{
    /// <summary>From the server's own lyrics; it has a Spotify track id.</summary>
    Local = 0,

    /// <summary>From a lyrics provider's search; the score is against the provider's excerpt.</summary>
    External = 1
}

/// <summary>
/// One scored song for <see cref="SongGuessRanker"/>.
/// </summary>
/// <param name="Origin">Local lyrics or an external provider.</param>
/// <param name="SourceName">"Local" or the provider name.</param>
/// <param name="SpotifyTrackId">The Spotify track id for local candidates.</param>
/// <param name="Title">Track title as stored or returned by the provider.</param>
/// <param name="Artist">Artist names as stored or returned by the provider.</param>
/// <param name="Album">Album name, when known.</param>
/// <param name="DurationMs">Track duration, when the source knows it.</param>
/// <param name="Score">Fragment match score from <see cref="LyricFragmentScorer"/>.</param>
/// <param name="Verified">False for a provider hit the scorer could not confirm.</param>
/// <param name="MatchedLines">The original lines of the matched window.</param>
/// <param name="LyricTokens">Normalized tokens of the full lyric (local) or excerpt (external).</param>
public sealed record SongCandidate(
    SongCandidateOrigin Origin,
    string SourceName,
    string? SpotifyTrackId,
    string Title,
    string Artist,
    string? Album,
    int? DurationMs,
    double Score,
    bool Verified,
    IReadOnlyList<string> MatchedLines,
    IReadOnlyList<string> LyricTokens);

public enum SongMatchOutcome
{
    /// <summary>The top guess is confident and clearly ahead of the next different song.</summary>
    Accepted = 0,

    /// <summary>Plausible guesses, but not confident or not clearly ahead.</summary>
    Ambiguous = 1,

    /// <summary>No guess is good enough.</summary>
    None = 2
}

/// <summary>Versions of one work: the best-ranked candidate and every candidate grouped with it.</summary>
public sealed record SongGuessGroup(SongCandidate Head, IReadOnlyList<SongCandidate> Members)
{
    public int VersionCount => Members.Count;
}

/// <summary>Ranked groups and the decision taken on their heads.</summary>
/// <param name="Outcome">Accepted, ambiguous or none.</param>
/// <param name="TopScore">Score of the best group head, or 0.</param>
/// <param name="Margin">Top score minus the second group head's score (0 when absent).</param>
/// <param name="Guesses">Scored groups first, then unverified provider hits.</param>
public sealed record SongGuessRanking(
    SongMatchOutcome Outcome,
    double TopScore,
    double Margin,
    IReadOnlyList<SongGuessGroup> Guesses);
