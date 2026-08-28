namespace Cantus.Core.Models;

public sealed record UserSession
{
    public required string Id { get; init; }
    public required string SpotifyUserId { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public string? ProfileImageUrl { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public override string ToString() =>
        $"UserSession {{ Id = {Id}, SpotifyUserId = {SpotifyUserId}, DisplayName = {DisplayName}, Email = {Email}, AccessToken = [REDACTED], RefreshToken = [REDACTED], ExpiresAtUtc = {ExpiresAtUtc} }}";
}
