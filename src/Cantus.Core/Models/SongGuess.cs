using Cantus.Core.Search;

namespace Cantus.Core.Models;

/// <summary>Where a song lookup may search.</summary>
public enum SongLookupScope
{
    /// <summary>The server's own lyrics only; no provider is called and no online budget is spent.</summary>
    Local = 0,

    /// <summary>The server's own lyrics, then the lyrics providers when the local match is not accepted.</summary>
    Auto = 1,

    /// <summary>The server's own lyrics and the lyrics providers, even when the local match is accepted.</summary>
    Online = 2
}

/// <summary>What one lyrics provider contributed to a lookup.</summary>
public enum SongLookupProviderState
{
    /// <summary>The provider answered, possibly from the query cache.</summary>
    Ok = 0,

    /// <summary>The provider was not needed for this lookup.</summary>
    Skipped = 1,

    /// <summary>The provider is not configured or its fragment search is switched off.</summary>
    Disabled = 2,

    /// <summary>The provider failed, rejected the request or ran out of time.</summary>
    Unavailable = 3,

    /// <summary>The provider is cooling down, or the caller's online lookup budget is spent.</summary>
    RateLimited = 4
}

/// <summary>
/// One ranked answer to a lookup. <see cref="Confidence"/> is a fragment match
/// score from 0 to 1, not a probability.
/// </summary>
/// <param name="Source">"Local" or the lyrics provider's name.</param>
/// <param name="SpotifyTrackId">The Spotify track id for local guesses; null for provider guesses.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist names.</param>
/// <param name="Album">Album name, when known.</param>
/// <param name="DurationMs">Track duration, when the source knows it (never for local guesses).</param>
/// <param name="Confidence">Fragment match score of the best version.</param>
/// <param name="Verified">False for a provider hit the scorer could not confirm.</param>
/// <param name="MatchedLines">The original lyric lines that matched (at most two).</param>
/// <param name="VersionCount">Versions of the same work grouped into this guess.</param>
public sealed record SongGuess(
    string Source,
    string? SpotifyTrackId,
    string Title,
    string Artist,
    string? Album,
    int? DurationMs,
    double Confidence,
    bool Verified,
    IReadOnlyList<string> MatchedLines,
    int VersionCount);

/// <summary>How one lyrics provider took part in a lookup.</summary>
/// <param name="ProviderName">The provider's name.</param>
/// <param name="State">What the provider contributed.</param>
/// <param name="RetryAfter">When rate limited, how long until it may be asked again.</param>
public sealed record SongLookupProviderStatus(
    string ProviderName,
    SongLookupProviderState State,
    TimeSpan? RetryAfter = null);

/// <summary>The answer to a song lookup by remembered lyrics.</summary>
/// <param name="Outcome">Accepted, ambiguous or none, decided on the merged guesses.</param>
/// <param name="OnlineRecommended">
/// True only for a local-scope lookup that was not accepted while at least one
/// provider is enabled and the caller's online budget is not spent.
/// </param>
/// <param name="Guesses">Ranked guesses, unverified provider hits last.</param>
/// <param name="Providers">One status per known lyrics provider, in search order.</param>
public sealed record SongLookupResult(
    SongMatchOutcome Outcome,
    bool OnlineRecommended,
    IReadOnlyList<SongGuess> Guesses,
    IReadOnlyList<SongLookupProviderStatus> Providers);
