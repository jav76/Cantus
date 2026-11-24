using System.Text.Json.Serialization;

namespace Cantus.Client.Services;

/// <summary>Hub <c>PublishLocalPlayback</c>: what this computer's followed player is doing.</summary>
public sealed class LocalPlaybackUpdatePayload
{
    /// <summary>Strictly increasing per hub connection, starting at 1.</summary>
    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }

    [JsonPropertyName("player")]
    public LocalPlayerInfoPayload Player { get; set; } = new();

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    /// <summary>Null when there is nothing worth publishing.</summary>
    [JsonPropertyName("track")]
    public LocalTrackPayload? Track { get; set; }

    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("positionMs")]
    public long PositionMs { get; set; }

    [JsonPropertyName("rate")]
    public double Rate { get; set; } = 1.0;

    /// <summary>Unix ms on the server clock when <see cref="PositionMs"/> was sampled; null before clock sync.</summary>
    [JsonPropertyName("positionAtServerMs")]
    public long? PositionAtServerMs { get; set; }

    [JsonPropertyName("capabilities")]
    public PlayerCapabilitiesPayload Capabilities { get; set; } = new();
}

public sealed class LocalPlayerInfoPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class LocalTrackPayload
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("albumArtUrl")]
    public string? AlbumArtUrl { get; set; }

    [JsonPropertyName("spotifyTrackId")]
    public string? SpotifyTrackId { get; set; }
}

/// <summary>The followed local player's transport controls; also echoed on <see cref="PlaybackStatePayload"/>.</summary>
public sealed class PlayerCapabilitiesPayload
{
    [JsonPropertyName("canPlay")]
    public bool CanPlay { get; set; }

    [JsonPropertyName("canPause")]
    public bool CanPause { get; set; }

    [JsonPropertyName("canGoNext")]
    public bool CanGoNext { get; set; }

    [JsonPropertyName("canGoPrevious")]
    public bool CanGoPrevious { get; set; }

    [JsonPropertyName("canSeek")]
    public bool CanSeek { get; set; }
}

/// <summary><c>POST /api/auth/local</c> request body.</summary>
public sealed class LocalSessionRequestPayload
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("joinCode")]
    public string? JoinCode { get; set; }

    public override string ToString() =>
        $"LocalSessionRequestPayload {{ DisplayName = {DisplayName}, JoinCode = [REDACTED] }}";
}

/// <summary>The <c>{ "error": "..." }</c> body the auth endpoints refuse with.</summary>
public sealed class ApiErrorPayload
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
