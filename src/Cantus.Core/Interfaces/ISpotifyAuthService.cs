using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ISpotifyAuthService
{
    /// <summary>
    /// Builds the Spotify authorize URL. <paramref name="userClientId"/> is the user's own
    /// Spotify app; null uses the server's. Throws <see cref="Models.SpotifyNotConfiguredException"/>
    /// when neither is set.
    /// </summary>
    Uri GetAuthorizationUri(
        string state,
        string codeChallenge,
        string? redirectUri = null,
        string? userClientId = null);

    /// <summary>
    /// Exchanges the callback code for tokens and stores the Session. Pass the same
    /// <paramref name="userClientId"/> the authorize URL was built with: it is saved on the
    /// Session so later refreshes use the app that issued the tokens.
    /// </summary>
    Task<UserSession> ExchangeCodeAsync(
        [Redact] string code,
        [Redact] string codeVerifier,
        string? redirectUri = null,
        string? userClientId = null,
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
    /// Returns the revoked session's kind, or null when no session had this id.
    /// </summary>
    Task<SessionKind?> RevokeSessionAsync([Redact] string sessionId, CancellationToken cancellationToken = default);
}

