namespace Cantus.Server.Models;

/// <summary>
/// A song lookup by remembered lyrics. Every member is optional on the wire so a missing
/// field is a 400 from the handler rather than a deserialization failure.
/// </summary>
public sealed record SongLookupSearchRequestDto
{
    public const string SCOPE_LOCAL = "local";
    public const string SCOPE_AUTO = "auto";
    public const string SCOPE_ONLINE = "online";

    public string? Fragment { get; init; }

    /// <summary>"local", "auto" or "online"; missing means "auto".</summary>
    public string? Scope { get; init; }
}

/// <summary>
/// Plays or queues a guess: by <see cref="SpotifyTrackId"/> for a library guess, otherwise by
/// <see cref="Title"/> and <see cref="Artist"/>, which are resolved on Spotify first.
/// </summary>
public sealed record SongLookupPlayRequestDto
{
    public const string ACTION_PLAY = "play";
    public const string ACTION_QUEUE = "queue";

    public string? Action { get; init; }
    public string? SpotifyTrackId { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public int? DurationMs { get; init; }
}

/// <summary>The ranked answer to a song lookup.</summary>
public sealed record SongLookupResponseDto
{
    /// <summary>"accepted", "ambiguous" or "none".</summary>
    public required string Outcome { get; init; }

    public bool OnlineRecommended { get; init; }
    public required IReadOnlyList<SongGuessDto> Guesses { get; init; }
    public required IReadOnlyList<SongLookupProviderDto> Providers { get; init; }
}

/// <summary>
/// One guess. <see cref="Confidence"/> is a fragment match score from 0 to 1, not a probability.
/// </summary>
public sealed record SongGuessDto
{
    /// <summary>"Local", "NetEase" or "LRCLIB".</summary>
    public required string Source { get; init; }

    public string? SpotifyTrackId { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Album { get; init; }

    /// <summary>Null for library guesses: the lyrics cache does not store a duration.</summary>
    public int? DurationMs { get; init; }

    public double Confidence { get; init; }
    public bool Verified { get; init; }
    public required IReadOnlyList<string> MatchedLines { get; init; }
    public int VersionCount { get; init; }
}

/// <summary>How one lyrics provider took part in a lookup.</summary>
public sealed record SongLookupProviderDto
{
    public required string Name { get; init; }

    /// <summary>"ok", "skipped", "disabled", "unavailable" or "rateLimited".</summary>
    public required string Status { get; init; }

    public int? RetryAfterSeconds { get; init; }
}

/// <summary>The outcome of playing or queueing a guess.</summary>
public sealed record SongLookupPlayResponseDto
{
    /// <summary>
    /// "success", "notOnSpotify", "noActiveDevice", "missingPermissions", "rateLimited" or "failed".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>The Spotify track a title and artist resolved to; null for a play by id.</summary>
    public TrackInfoDto? Track { get; init; }

    public int? RetryAfterSeconds { get; init; }
}
