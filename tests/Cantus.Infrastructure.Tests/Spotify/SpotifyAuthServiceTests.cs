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
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public sealed class SpotifyAuthServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CantusDbContext _dbContext;
    private readonly ITokenEncryptionService _encryptionService;
    private readonly IOptions<SpotifyOptions> _options;
    private readonly SpotifyAuthService _authService;

    public SpotifyAuthServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new CantusDbContext(dbOptions);
        _dbContext.Database.EnsureCreated();

        _encryptionService = new DataProtectionTokenEncryptionService(new EphemeralDataProtectionProvider());
        _options = Options.Create(new SpotifyOptions
        {
            ClientId = "test_spotify_client_id",
            ClientSecret = "test_spotify_client_secret",
            RedirectUri = "http://localhost:5000/api/auth/spotify/callback",
            Scopes = ["user-read-playback-state", "user-read-currently-playing"]
        });

        _authService = new SpotifyAuthService(_dbContext, _encryptionService, _options, NullLogger<SpotifyAuthService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void GetAuthorizationUri_ReturnsValidSpotifyOAuthUrlWithPKCE()
    {
        string state = "random_state_123";
        string codeChallenge = "test_code_challenge_hash";

        var uri = _authService.GetAuthorizationUri(state, codeChallenge);

        uri.Should().NotBeNull();
        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("accounts.spotify.com");
        uri.AbsolutePath.Should().Be("/authorize");

        string query = uri.Query;
        query.Should().Contain("client_id=test_spotify_client_id");
        query.Should().Contain("state=random_state_123");
        query.Should().Contain("code_challenge=test_code_challenge_hash");
        query.ToLowerInvariant().Should().Contain("redirect_uri=http%3a%2f%2flocalhost%3a5000%2fapi%2fauth%2fspotify%2fcallback");
    }

    [Fact]
    public async Task GetSessionAsync_WhenSessionExists_DecryptsAndReturnsSession()
    {
        string rawAccessToken = "access_token_secret_123";
        string rawRefreshToken = "refresh_token_secret_456";

        var entity = new UserSessionEntity
        {
            Id = "user_abc",
            SpotifyUserId = "spotify_user_999",
            DisplayName = "Test User",
            Email = "test@example.com",
            EncryptedAccessToken = _encryptionService.Encrypt(rawAccessToken),
            EncryptedRefreshToken = _encryptionService.Encrypt(rawRefreshToken),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _dbContext.UserSessions.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        var session = await _authService.GetSessionAsync("user_abc");

        session.Should().NotBeNull();
        session!.Id.Should().Be("user_abc");
        session.SpotifyUserId.Should().Be("spotify_user_999");
        session.DisplayName.Should().Be("Test User");
        session.AccessToken.Should().Be(rawAccessToken);
        session.RefreshToken.Should().Be(rawRefreshToken);
    }

    [Fact]
    public async Task GetSessionAsync_WhenSessionNotFound_ReturnsNull()
    {
        var session = await _authService.GetSessionAsync("nonexistent_user");
        session.Should().BeNull();
    }
}
