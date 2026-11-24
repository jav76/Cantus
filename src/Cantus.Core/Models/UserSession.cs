using Cantus.Core.Logging;

namespace Cantus.Core.Models;

public sealed record UserSession
{
    public required string Id { get; init; }
    public SessionKind Kind { get; init; } = SessionKind.Spotify;

    /// <summary>The Spotify account id. Null for a Local Session.</summary>
    public string? SpotifyUserId { get; init; }

    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public string? ProfileImageUrl { get; init; }

    /// <summary>The Spotify access token. Null for a Local Session.</summary>
    public string? AccessToken { get; init; }

    /// <summary>The Spotify refresh token. Null for a Local Session.</summary>
    public string? RefreshToken { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public override string ToString() =>
        $"UserSession {{ Id = {SessionLogRef.For(Id)}, Kind = {Kind}, SpotifyUserId = {SpotifyUserId}, DisplayName = {DisplayName}, Email = {Email}, AccessToken = [REDACTED], RefreshToken = [REDACTED], ExpiresAtUtc = {ExpiresAtUtc} }}";
}
