namespace Cantus.Server.Models;

/// <summary>
/// What a desktop client's followed player is doing, published over the hub by a Local
/// Session. The server validates and clamps every field before use.
/// </summary>
public sealed record LocalPlaybackUpdateDto
{
    /// <summary>Strictly increasing per hub connection, starting at 1.</summary>
    public long Sequence { get; init; }

    public LocalPlayerDto? Player { get; init; }
    public string? DeviceName { get; init; }

    /// <summary>Null when nothing is worth publishing: no player, stopped, or a filtered track.</summary>
    public LocalTrackDto? Track { get; init; }

    public bool IsPlaying { get; init; }
    public long PositionMs { get; init; }
    public double Rate { get; init; } = 1.0;

    /// <summary>
    /// Unix milliseconds on the server clock at the moment <see cref="PositionMs"/> was sampled,
    /// or null before the client has synced its clock.
    /// </summary>
    public long? PositionAtServerMs { get; init; }

    public PlaybackCapabilitiesDto? Capabilities { get; init; }
}

public sealed record LocalPlayerDto
{
    public string? Id { get; init; }
    public string? Name { get; init; }
}

public sealed record LocalTrackDto
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public long DurationMs { get; init; }
    public string? AlbumArtUrl { get; init; }
    public string? SpotifyTrackId { get; init; }
}

/// <summary>Body of <c>POST /api/auth/local</c>.</summary>
public sealed record LocalSessionRequestDto
{
    public string? DisplayName { get; init; }
    public string? JoinCode { get; init; }

    public override string ToString() =>
        $"LocalSessionRequestDto {{ DisplayName = {DisplayName}, JoinCode = [REDACTED] }}";
}

/// <summary>A machine-readable error body, such as <c>{ "error": "join_code_required" }</c>.</summary>
public sealed record ApiErrorDto(string Error);
