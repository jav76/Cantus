using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ISpotifyAuthService
{
    Uri GetAuthorizationUri(string state, string codeChallenge, string? redirectUri = null);
    Task<UserSession> ExchangeCodeAsync(
        [Redact] string code,
        [Redact] string codeVerifier,
        string? redirectUri = null,
        CancellationToken cancellationToken = default);
    Task<UserSession> RefreshTokenAsync([Redact] string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a request credential (cookie, query or header value) to a session. Matches the
    /// exact session id only, so a Spotify user id is never accepted as a credential.
    /// </summary>
    Task<UserSession?> GetSessionByIdAsync(
        [Redact] string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a session for a user id the server already trusts, such as a registry
    /// subscription. Matches the session id or the Spotify user id. Never pass a request
    /// credential here; use <see cref="GetSessionByIdAsync"/> instead.
    /// </summary>
    Task<UserSession?> GetSessionForUserAsync([Redact] string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSession>> GetAllSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the session with this exact session id. A Spotify user id does not match.
    /// </summary>
    Task<bool> RevokeSessionAsync([Redact] string sessionId, CancellationToken cancellationToken = default);
}

