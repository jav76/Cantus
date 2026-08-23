namespace Cantus.Server.Models;

public sealed record AuthorizedSessionDto
{
    public required string Id { get; init; }
    public required string SpotifyUserId { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public string? ProfileImageUrl { get; init; }
    public bool IsCurrentlyPlaying { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
