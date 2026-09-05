using Cantus.Core.Interfaces;
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, UserSession> _sessionCache = new();
    private readonly CantusDbContext _dbContext;
    private readonly ITokenEncryptionService _encryptionService;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SpotifyAuthService> _logger;

    public SpotifyAuthService(
        CantusDbContext dbContext,
        ITokenEncryptionService encryptionService,
        IOptions<SpotifyOptions> options,
        ILogger<SpotifyAuthService> logger)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _options = options.Value;
        _logger = logger;
    }

    public Uri GetAuthorizationUri(string state, string codeChallenge, string? redirectUri = null)
    {
        string effectiveRedirectUri = !string.IsNullOrWhiteSpace(redirectUri) ? redirectUri : _options.RedirectUri;
        LoginRequest loginRequest = new(
            new Uri(effectiveRedirectUri),
            _options.ClientId,
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
        CancellationToken cancellationToken = default)
    {
        string effectiveRedirectUri = !string.IsNullOrWhiteSpace(redirectUri) ? redirectUri : _options.RedirectUri;
        OAuthClient oauth = new();
        PKCETokenRequest tokenRequest = new(_options.ClientId, code, new Uri(effectiveRedirectUri), codeVerifier);
        PKCETokenResponse tokenResponse = await oauth.RequestToken(tokenRequest, cancellationToken);

        SpotifyClient spotify = new(tokenResponse.AccessToken);
        PrivateUser me = await spotify.UserProfile.Current(cancellationToken);

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
            throw new InvalidOperationException($"User session '{userId}' not found.");
        }

        string rawRefreshToken = _encryptionService.Decrypt(sessionEntity.EncryptedRefreshToken);
        OAuthClient oauth = new();
        PKCETokenRefreshRequest refreshRequest = new(_options.ClientId, rawRefreshToken);
        PKCETokenResponse refreshResponse = await oauth.RequestToken(refreshRequest, cancellationToken);

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

    public async Task<UserSession?> GetSessionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (_sessionCache.TryGetValue(userId, out UserSession? cached) && cached is not null)
        {
            if (cached.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached;
            }
        }

        UserSessionEntity? sessionEntity = await _dbContext.UserSessions
            .FirstOrDefaultAsync(u => u.Id == userId || u.SpotifyUserId == userId, cancellationToken);

        if (sessionEntity is null)
        {
            _sessionCache.TryRemove(userId, out _);
            return null;
        }

        // If expired or about to expire in under 2 minutes, refresh it automatically
        if (sessionEntity.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            try
            {
                return await RefreshTokenAsync(sessionEntity.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-refresh expired token for user {UserId}", sessionEntity.Id);
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
        string userId,
        CancellationToken cancellationToken = default)
    {
        UserSessionEntity? entity = await _dbContext.UserSessions
            .FirstOrDefaultAsync(u => u.Id == userId || u.SpotifyUserId == userId, cancellationToken);

        if (entity is null)
        {
            _sessionCache.TryRemove(userId, out _);
            return false;
        }

        _sessionCache.TryRemove(entity.Id, out _);
        _sessionCache.TryRemove(entity.SpotifyUserId, out _);

        _dbContext.UserSessions.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static void ClearCache()
    {
        _sessionCache.Clear();
    }
}
