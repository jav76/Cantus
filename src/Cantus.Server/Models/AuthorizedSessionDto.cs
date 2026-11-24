namespace Cantus.Server.Models;

public sealed record AuthorizedSessionDto
{
    public const string KIND_SPOTIFY = "Spotify";
    public const string KIND_LOCAL = "Local";

    public required string Id { get; init; }

    /// <summary><see cref="KIND_SPOTIFY"/> or <see cref="KIND_LOCAL"/>.</summary>
    public string Kind { get; init; } = KIND_SPOTIFY;

    /// <summary>Null for a Local Session.</summary>
    public string? SpotifyUserId { get; init; }

    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public string? ProfileImageUrl { get; init; }
    public bool IsCurrentlyPlaying { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
