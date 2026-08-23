namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class UserSessionEntity
{
    public required string Id { get; set; }
    public required string SpotifyUserId { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public string? ProfileImageUrl { get; set; }
    public required string EncryptedAccessToken { get; set; }
    public required string EncryptedRefreshToken { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
