using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cantus.Client.Services;

public sealed class LyricsCandidatePayload
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("providerCandidateId")]
    public string ProviderCandidateId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("isSynced")]
    public bool IsSynced { get; set; }

    [JsonPropertyName("isInstrumental")]
    public bool IsInstrumental { get; set; }

    [JsonPropertyName("preview")]
    public List<string> Preview { get; set; } = new();
}

/// <summary>The override in effect for a track, as the candidates response reports it.</summary>
public sealed class LyricsActiveOverridePayload
{
    public const string KIND_CANDIDATE = "candidate";
    public const string KIND_NO_LYRICS = "noLyrics";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("candidateId")]
    public string? CandidateId { get; set; }
}

public sealed class LyricsCandidatesPayload
{
    [JsonPropertyName("trackId")]
    public string TrackId { get; set; } = string.Empty;

    [JsonPropertyName("candidates")]
    public List<LyricsCandidatePayload> Candidates { get; set; } = new();

    [JsonPropertyName("unavailableProviders")]
    public List<string> UnavailableProviders { get; set; } = new();

    [JsonPropertyName("activeOverride")]
    public LyricsActiveOverridePayload? ActiveOverride { get; set; }
}

/// <summary>
/// Body of <c>POST /api/lyrics/{trackId}/override</c>. Actions are sent as the
/// strings the server names, not as an enum.
/// </summary>
public sealed class LyricsOverrideRequestPayload
{
    public const string ACTION_CANDIDATE = "candidate";
    public const string ACTION_REFETCH = "refetch";
    public const string ACTION_NO_LYRICS = "noLyrics";
    public const string ACTION_CLEAR = "clear";

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("candidateId")]
    public string? CandidateId { get; set; }

    public static LyricsOverrideRequestPayload ForCandidate(string provider, string candidateId) => new()
    {
        Action = ACTION_CANDIDATE,
        Provider = provider,
        CandidateId = candidateId,
    };

    public static LyricsOverrideRequestPayload Refetch() => new() { Action = ACTION_REFETCH };

    public static LyricsOverrideRequestPayload NoLyrics() => new() { Action = ACTION_NO_LYRICS };

    public static LyricsOverrideRequestPayload Clear() => new() { Action = ACTION_CLEAR };
}
