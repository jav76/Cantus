using Cantus.Core.Models;

namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class UserSessionEntity
{
    public const int KIND_MAX_LENGTH = 32;

    public required string Id { get; set; }
    public SessionKind Kind { get; set; } = SessionKind.Spotify;
    public string? SpotifyUserId { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? EncryptedAccessToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }

    /// <summary>
    /// The user's own Spotify app that issued the tokens, or null for the server's app.
    /// Refreshing must use the same Client ID that issued the refresh token.
    /// </summary>
    public string? SpotifyClientId { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
