using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Security;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpotifyAPI.Web;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

/// <summary>Sign-in with the user's own Spotify app: which Client ID each Spotify call uses.</summary>
[Collection(SpotifyAuthServiceCacheCollection.NAME)]
public sealed class SpotifyOwnAppAuthServiceTests : IDisposable
{
    private const string SERVER_CLIENT_ID = "0123456789abcdef0123456789abcdef";
    private const string OWN_CLIENT_ID = "fedcba9876543210fedcba9876543210";
    private const string REDIRECT_URI = "http://127.0.0.1:5000/api/auth/spotify/callback";

    private readonly SqliteConnection _connection;
    private readonly CantusDbContext _dbContext;
    private readonly ITokenEncryptionService _encryptionService;
    private readonly FakeTokenClient _tokenClient = new();

    public SpotifyOwnAppAuthServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new CantusDbContext(new DbContextOptionsBuilder<CantusDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
        _encryptionService = new DataProtectionTokenEncryptionService(new EphemeralDataProtectionProvider());
    }

    public void Dispose()
    {
        SpotifyAuthService.ClearCache();
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void GetAuthorizationUri_WithOwnClientId_UsesIt()
    {
        SpotifyAuthService service = CreateService(SERVER_CLIENT_ID);

        Uri uri = service.GetAuthorizationUri("state", "challenge", REDIRECT_URI, OWN_CLIENT_ID);

        uri.Query.Should().Contain($"client_id={OWN_CLIENT_ID}");
    }

    [Fact]
    public void GetAuthorizationUri_WithNoClientIdAnywhere_ThrowsNotConfigured()
    {
        SpotifyAuthService service = CreateService(serverClientId: string.Empty);

        Action act = () => service.GetAuthorizationUri("state", "challenge", REDIRECT_URI);

        act.Should().Throw<SpotifyNotConfiguredException>();
    }

    [Fact]
    public async Task ExchangeCodeAsync_WithOwnClientId_ExchangesWithItAndStoresIt()
    {
        SpotifyAuthService service = CreateService(SERVER_CLIENT_ID);

        await service.ExchangeCodeAsync("code", "verifier", REDIRECT_URI, OWN_CLIENT_ID);

        _tokenClient.TokenRequests.Single().ClientId.Should().Be(OWN_CLIENT_ID);
        UserSessionEntity entity = await _dbContext.UserSessions.AsNoTracking().SingleAsync();
        entity.SpotifyClientId.Should().Be(OWN_CLIENT_ID);
    }

    [Fact]
    public async Task ExchangeCodeAsync_RelinkingThroughTheServerApp_ClearsTheStoredOwnClientId()
    {
        SpotifyAuthService service = CreateService(SERVER_CLIENT_ID);
        await service.ExchangeCodeAsync("code-1", "verifier", REDIRECT_URI, OWN_CLIENT_ID);

        await service.ExchangeCodeAsync("code-2", "verifier", REDIRECT_URI);

        _tokenClient.TokenRequests.Last().ClientId.Should().Be(SERVER_CLIENT_ID);
        UserSessionEntity entity = await _dbContext.UserSessions.AsNoTracking().SingleAsync();
        entity.SpotifyClientId.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_UsesTheClientIdThatIssuedTheRefreshToken()
    {
        SpotifyAuthService service = CreateService(SERVER_CLIENT_ID);
        await SeedSessionAsync("own-session", "own-user", OWN_CLIENT_ID);

        await service.RefreshTokenAsync("own-session");

        _tokenClient.RefreshRequests.Single().ClientId.Should().Be(OWN_CLIENT_ID);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithoutStoredClientId_UsesTheServerClientId()
    {
        SpotifyAuthService service = CreateService(SERVER_CLIENT_ID);
        await SeedSessionAsync("server-session", "server-user", spotifyClientId: null);

        await service.RefreshTokenAsync("server-session");

        _tokenClient.RefreshRequests.Single().ClientId.Should().Be(SERVER_CLIENT_ID);
    }

    [Fact]
    public async Task RefreshTokenAsync_ForOwnAppSession_WorksWithoutAServerClientId()
    {
        SpotifyAuthService service = CreateService(serverClientId: string.Empty);
        await SeedSessionAsync("own-session", "own-user", OWN_CLIENT_ID);

        UserSession session = await service.RefreshTokenAsync("own-session");

        session.AccessToken.Should().Be("refreshed-access");
        _tokenClient.RefreshRequests.Single().ClientId.Should().Be(OWN_CLIENT_ID);
    }

    private SpotifyAuthService CreateService(string serverClientId)
    {
        return new SpotifyAuthService(
            _dbContext,
            _encryptionService,
            Options.Create(new SpotifyOptions { ClientId = serverClientId, RedirectUri = REDIRECT_URI }),
            NullLogger<SpotifyAuthService>.Instance,
            _tokenClient);
    }

    private async Task SeedSessionAsync(string id, string spotifyUserId, string? spotifyClientId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await _dbContext.UserSessions.AddAsync(new UserSessionEntity
        {
            Id = id,
            SpotifyUserId = spotifyUserId,
            DisplayName = spotifyUserId,
            EncryptedAccessToken = _encryptionService.Encrypt("old-access"),
            EncryptedRefreshToken = _encryptionService.Encrypt("old-refresh"),
            SpotifyClientId = spotifyClientId,
            ExpiresAtUtc = now.AddMinutes(-1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await _dbContext.SaveChangesAsync();
    }

    private sealed class FakeTokenClient : ISpotifyTokenClient
    {
        public List<PKCETokenRequest> TokenRequests { get; } = new();

        public List<PKCETokenRefreshRequest> RefreshRequests { get; } = new();

        public Task<PKCETokenResponse> RequestTokenAsync(PKCETokenRequest request, CancellationToken cancellationToken)
        {
            TokenRequests.Add(request);
            return Task.FromResult(Response("access"));
        }

        public Task<PKCETokenResponse> RefreshTokenAsync(PKCETokenRefreshRequest request, CancellationToken cancellationToken)
        {
            RefreshRequests.Add(request);
            return Task.FromResult(Response("refreshed-access"));
        }

        public Task<PrivateUser> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PrivateUser { Id = "spotify-user", DisplayName = "Listener" });
        }

        private static PKCETokenResponse Response(string accessToken) => new()
        {
            AccessToken = accessToken,
            RefreshToken = "refresh",
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Scope = string.Empty,
        };
    }
}
