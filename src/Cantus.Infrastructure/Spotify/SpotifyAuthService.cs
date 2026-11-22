using System.Linq.Expressions;
using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyAPI.Web;

namespace Cantus.Infrastructure.Spotify;

public sealed class SpotifyAuthService : ISpotifyAuthService
{
    private const int TOKEN_REFRESH_WINDOW_MINUTES = 2;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, UserSession> _sessionCache = new();
    private readonly CantusDbContext _dbContext;
    private readonly ITokenEncryptionService _encryptionService;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SpotifyAuthService> _logger;
    private readonly ISpotifyTokenClient _tokenClient;

    public SpotifyAuthService(
        CantusDbContext dbContext,
        ITokenEncryptionService encryptionService,
        IOptions<SpotifyOptions> options,
        ILogger<SpotifyAuthService> logger,
        ISpotifyTokenClient? tokenClient = null)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _options = options.Value;
        _logger = logger;
        _tokenClient = tokenClient ?? new SpotifyTokenClient();
    }

    public Uri GetAuthorizationUri(
        string state,
        string codeChallenge,
        string? redirectUri = null,
        string? userClientId = null)
    {
        string effectiveRedirectUri = !string.IsNullOrWhiteSpace(redirectUri) ? redirectUri : _options.RedirectUri;
        LoginRequest loginRequest = new(
            new Uri(effectiveRedirectUri),
            ResolveClientId(userClientId),
            LoginRequest.ResponseType.Code)
        {
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            State = state,
            Scope = _options.Scopes
        };

        return loginRequest.ToUri();
    }

    public async Task<UserSession> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string? redirectUri = null,
        string? userClientId = null,
        CancellationToken cancellationToken = default)
    {
        string effectiveRedirectUri = !string.IsNullOrWhiteSpace(redirectUri) ? redirectUri : _options.RedirectUri;
        string clientId = ResolveClientId(userClientId);
        string? storedClientId = string.IsNullOrWhiteSpace(userClientId) ? null : clientId;
        PKCETokenRequest tokenRequest = new(clientId, code, new Uri(effectiveRedirectUri), codeVerifier);
        PKCETokenResponse tokenResponse = await _tokenClient.RequestTokenAsync(tokenRequest, cancellationToken);

        PrivateUser me = await _tokenClient.GetCurrentUserAsync(tokenResponse.AccessToken, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddSeconds(tokenResponse.ExpiresIn);

        string encAccessToken = _encryptionService.Encrypt(tokenResponse.AccessToken);
        string encRefreshToken = _encryptionService.Encrypt(tokenResponse.RefreshToken);

        UserSessionEntity? sessionEntity = await _dbContext.UserSessions
            .FirstOrDefaultAsync(u => u.SpotifyUserId == me.Id, cancellationToken);

        if (sessionEntity is null)
        {
            sessionEntity = new UserSessionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                SpotifyUserId = me.Id,
                DisplayName = me.DisplayName ?? me.Id,
                Email = me.Email,
                ProfileImageUrl = me.Images?.FirstOrDefault()?.Url,
                EncryptedAccessToken = encAccessToken,
                EncryptedRefreshToken = encRefreshToken,
                SpotifyClientId = storedClientId,
                ExpiresAtUtc = expiresAt,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            await _dbContext.UserSessions.AddAsync(sessionEntity, cancellationToken);
        }
        else
        {
            sessionEntity.DisplayName = me.DisplayName ?? me.Id;
            sessionEntity.Email = me.Email;
            sessionEntity.ProfileImageUrl = me.Images?.FirstOrDefault()?.Url;
            sessionEntity.EncryptedAccessToken = encAccessToken;
            sessionEntity.EncryptedRefreshToken = encRefreshToken;
            sessionEntity.SpotifyClientId = storedClientId;
            sessionEntity.ExpiresAtUtc = expiresAt;
            sessionEntity.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        UserSession session = new()
        {
            Id = sessionEntity.Id,
            SpotifyUserId = sessionEntity.SpotifyUserId,
            DisplayName = sessionEntity.DisplayName,
            Email = sessionEntity.Email,
            ProfileImageUrl = sessionEntity.ProfileImageUrl,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAtUtc = sessionEntity.ExpiresAtUtc,
            CreatedAtUtc = sessionEntity.CreatedAtUtc,
            UpdatedAtUtc = sessionEntity.UpdatedAtUtc
        };

        _sessionCache[session.Id] = session;
        _sessionCache[session.SpotifyUserId] = session;
        return session;
    }

    public async Task<UserSession> RefreshTokenAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        UserSessionEntity? sessionEntity = await _dbContext.UserSessions
            .FirstOrDefaultAsync(u => u.Id == userId || u.SpotifyUserId == userId, cancellationToken);

        if (sessionEntity is null)
        {
            throw new InvalidOperationException($"User session {SessionLogRef.For(userId)} not found.");
        }

        string rawRefreshToken = _encryptionService.Decrypt(sessionEntity.EncryptedRefreshToken);
        PKCETokenRefreshRequest refreshRequest = new(ResolveClientId(sessionEntity.SpotifyClientId), rawRefreshToken);
        PKCETokenResponse refreshResponse = await _tokenClient.RefreshTokenAsync(refreshRequest, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        sessionEntity.EncryptedAccessToken = _encryptionService.Encrypt(refreshResponse.AccessToken);
        if (!string.IsNullOrEmpty(refreshResponse.RefreshToken))
        {
            sessionEntity.EncryptedRefreshToken = _encryptionService.Encrypt(refreshResponse.RefreshToken);
        }
        sessionEntity.ExpiresAtUtc = now.AddSeconds(refreshResponse.ExpiresIn);
        sessionEntity.UpdatedAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        UserSession session = new()
        {
            Id = sessionEntity.Id,
            SpotifyUserId = sessionEntity.SpotifyUserId,
            DisplayName = sessionEntity.DisplayName,
            Email = sessionEntity.Email,
            ProfileImageUrl = sessionEntity.ProfileImageUrl,
            AccessToken = refreshResponse.AccessToken,
            RefreshToken = string.IsNullOrEmpty(refreshResponse.RefreshToken)
                ? rawRefreshToken
                : refreshResponse.RefreshToken,
            ExpiresAtUtc = sessionEntity.ExpiresAtUtc,
            CreatedAtUtc = sessionEntity.CreatedAtUtc,
            UpdatedAtUtc = sessionEntity.UpdatedAtUtc
        };

        _sessionCache[session.Id] = session;
        _sessionCache[session.SpotifyUserId] = session;
        return session;
    }

    public Task<UserSession?> GetSessionByIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        // The cache is also keyed by Spotify user id, so a hit only counts when the
        // cached session's own id matches.
        return GetSessionCoreAsync(
            sessionId,
            cached => string.Equals(cached.Id, sessionId, StringComparison.Ordinal),
            u => u.Id == sessionId,
            cancellationToken);
    }

    public Task<UserSession?> GetSessionForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return GetSessionCoreAsync(
            userId,
            _ => true,
            u => u.Id == userId || u.SpotifyUserId == userId,
            cancellationToken);
    }

    private async Task<UserSession?> GetSessionCoreAsync(
        string key,
        Func<UserSession, bool> cacheEntryMatches,
        Expression<Func<UserSessionEntity, bool>> entityMatches,
        CancellationToken cancellationToken)
    {
        DateTimeOffset refreshThreshold = DateTimeOffset.UtcNow.AddMinutes(TOKEN_REFRESH_WINDOW_MINUTES);
        UserSession? cached = _sessionCache.TryGetValue(key, out UserSession? entry) && cacheEntryMatches(entry)
            ? entry
            : null;

        if (cached is not null && cached.ExpiresAtUtc > refreshThreshold)
        {
            return cached;
        }

        UserSessionEntity? sessionEntity = await _dbContext.UserSessions
            .FirstOrDefaultAsync(entityMatches, cancellationToken);

        if (sessionEntity is null)
        {
            if (cached is not null)
            {
                _sessionCache.TryRemove(key, out _);
            }

            return null;
        }

        if (sessionEntity.ExpiresAtUtc <= refreshThreshold)
        {
            try
            {
                return await RefreshTokenAsync(sessionEntity.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to auto-refresh expired token for Spotify user {SpotifyUserId} (Session: {SessionRef})",
                    sessionEntity.SpotifyUserId,
                    SessionLogRef.For(sessionEntity.Id));
            }
        }

        UserSession loadedSession = new()
        {
            Id = sessionEntity.Id,
            SpotifyUserId = sessionEntity.SpotifyUserId,
            DisplayName = sessionEntity.DisplayName,
            Email = sessionEntity.Email,
            ProfileImageUrl = sessionEntity.ProfileImageUrl,
            AccessToken = _encryptionService.Decrypt(sessionEntity.EncryptedAccessToken),
            RefreshToken = _encryptionService.Decrypt(sessionEntity.EncryptedRefreshToken),
            ExpiresAtUtc = sessionEntity.ExpiresAtUtc,
            CreatedAtUtc = sessionEntity.CreatedAtUtc,
            UpdatedAtUtc = sessionEntity.UpdatedAtUtc
        };

        _sessionCache[loadedSession.Id] = loadedSession;
        _sessionCache[loadedSession.SpotifyUserId] = loadedSession;
        return loadedSession;
    }

    public async Task<IReadOnlyList<UserSession>> GetAllSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        List<UserSessionEntity> entities = await _dbContext.UserSessions
            .ToListAsync(cancellationToken);

        List<UserSession> list = new(entities.Count);
        foreach (UserSessionEntity entity in entities.OrderByDescending(u => u.UpdatedAtUtc))
        {
            list.Add(new UserSession
            {
                Id = entity.Id,
                SpotifyUserId = entity.SpotifyUserId,
                DisplayName = entity.DisplayName,
                Email = entity.Email,
                ProfileImageUrl = entity.ProfileImageUrl,
                AccessToken = _encryptionService.Decrypt(entity.EncryptedAccessToken),
                RefreshToken = _encryptionService.Decrypt(entity.EncryptedRefreshToken),
                ExpiresAtUtc = entity.ExpiresAtUtc,
                CreatedAtUtc = entity.CreatedAtUtc,
                UpdatedAtUtc = entity.UpdatedAtUtc
            });
        }

        return list;
    }

    public async Task<bool> RevokeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        UserSessionEntity? entity = await _dbContext.UserSessions
            .FirstOrDefaultAsync(u => u.Id == sessionId, cancellationToken);

        if (entity is null)
        {
            if (_sessionCache.TryGetValue(sessionId, out UserSession? cached)
                && string.Equals(cached.Id, sessionId, StringComparison.Ordinal))
            {
                _sessionCache.TryRemove(sessionId, out _);
            }

            return false;
        }

        _sessionCache.TryRemove(entity.Id, out _);
        _sessionCache.TryRemove(entity.SpotifyUserId, out _);

        _dbContext.UserSessions.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private string ResolveClientId(string? userClientId)
    {
        string clientId = string.IsNullOrWhiteSpace(userClientId) ? _options.ClientId : userClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new SpotifyNotConfiguredException();
        }

        return clientId;
    }

    public static void ClearCache()
    {
        _sessionCache.Clear();
    }
}
