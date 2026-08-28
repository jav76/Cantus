using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ISpotifyAuthService
{
    Uri GetAuthorizationUri(string state, string codeChallenge, string? redirectUri = null);
    Task<UserSession> ExchangeCodeAsync(
        string code,
        [Redact] string codeVerifier,
        string? redirectUri = null,
        CancellationToken cancellationToken = default);
    Task<UserSession> RefreshTokenAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserSession?> GetSessionAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSession>> GetAllSessionsAsync(CancellationToken cancellationToken = default);
    Task<bool> RevokeSessionAsync(string userId, CancellationToken cancellationToken = default);
}

