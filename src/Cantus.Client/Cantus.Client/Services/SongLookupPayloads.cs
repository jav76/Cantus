using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cantus.Client.Services;

/// <summary>Body of <c>POST /api/lookup/search</c>. Wire values are strings; there are no enums.</summary>
public sealed record SongLookupSearchRequestPayload
{
    public const string SCOPE_LOCAL = "local";
    public const string SCOPE_AUTO = "auto";
    public const string SCOPE_ONLINE = "online";

    [JsonPropertyName("fragment")]
    public string? Fragment { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

/// <summary>
/// Body of <c>POST /api/lookup/play</c>: a library guess by <see cref="SpotifyTrackId"/>,
/// otherwise by <see cref="Title"/> and <see cref="Artist"/>, which the server resolves on Spotify.
/// </summary>
public sealed record SongLookupPlayRequestPayload
{
    public const string ACTION_PLAY = "play";
    public const string ACTION_QUEUE = "queue";

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("spotifyTrackId")]
    public string? SpotifyTrackId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("durationMs")]
    public int? DurationMs { get; init; }
}

/// <summary>The ranked answer to a song lookup.</summary>
public sealed record SongLookupResponsePayload
{
    public const string OUTCOME_ACCEPTED = "accepted";
    public const string OUTCOME_AMBIGUOUS = "ambiguous";
    public const string OUTCOME_NONE = "none";

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = OUTCOME_NONE;

    [JsonPropertyName("onlineRecommended")]
    public bool OnlineRecommended { get; init; }

    [JsonPropertyName("guesses")]
    public IReadOnlyList<SongGuessPayload> Guesses { get; init; } = Array.Empty<SongGuessPayload>();

    [JsonPropertyName("providers")]
    public IReadOnlyList<SongLookupProviderPayload> Providers { get; init; } =
        Array.Empty<SongLookupProviderPayload>();
}

/// <summary>One guess. <see cref="Confidence"/> is a fragment match score from 0 to 1, not a probability.</summary>
public sealed record SongGuessPayload
{
    public const string SOURCE_LOCAL = "Local";

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("spotifyTrackId")]
    public string? SpotifyTrackId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; init; } = string.Empty;

    [JsonPropertyName("album")]
    public string? Album { get; init; }

    /// <summary>Null for library guesses: the lyrics cache does not store a duration.</summary>
    [JsonPropertyName("durationMs")]
    public int? DurationMs { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("verified")]
    public bool Verified { get; init; }

    [JsonPropertyName("matchedLines")]
    public IReadOnlyList<string> MatchedLines { get; init; } = Array.Empty<string>();

    [JsonPropertyName("versionCount")]
    public int VersionCount { get; init; }
}

/// <summary>How one lyrics provider took part in a lookup.</summary>
public sealed record SongLookupProviderPayload
{
    public const string STATUS_OK = "ok";
    public const string STATUS_SKIPPED = "skipped";
    public const string STATUS_DISABLED = "disabled";
    public const string STATUS_UNAVAILABLE = "unavailable";
    public const string STATUS_RATE_LIMITED = "rateLimited";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = STATUS_UNAVAILABLE;

    [JsonPropertyName("retryAfterSeconds")]
    public int? RetryAfterSeconds { get; init; }
}

/// <summary>The outcome of playing or queueing a guess.</summary>
public sealed record SongLookupPlayResponsePayload
{
    public const string STATUS_SUCCESS = "success";
    public const string STATUS_NOT_ON_SPOTIFY = "notOnSpotify";
    public const string STATUS_NO_ACTIVE_DEVICE = "noActiveDevice";
    public const string STATUS_MISSING_PERMISSIONS = "missingPermissions";
    public const string STATUS_RATE_LIMITED = "rateLimited";
    public const string STATUS_FAILED = "failed";

    [JsonPropertyName("status")]
    public string Status { get; init; } = STATUS_FAILED;

    /// <summary>The Spotify track a title and artist resolved to; null for a play by id.</summary>
    [JsonPropertyName("track")]
    public TrackInfoPayload? Track { get; init; }

    [JsonPropertyName("retryAfterSeconds")]
    public int? RetryAfterSeconds { get; init; }
}

/// <summary>The <c>{ "message": "..." }</c> body of a 400 from the song lookup endpoints.</summary>
public sealed record SongLookupErrorPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
