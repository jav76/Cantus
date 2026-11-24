using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Security;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

/// <summary>
/// A Local Session has no Spotify account or tokens. The auth service must load it without
/// refreshing or decrypting anything, and must never cache it under a shared null key.
/// Ids are unique per test because the session cache is static.
/// </summary>
[Collection(SpotifyAuthServiceCacheCollection.NAME)]
public sealed class SpotifyAuthServiceLocalSessionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CantusDbContext _dbContext;
    private readonly ITokenEncryptionService _encryptionService = Substitute.For<ITokenEncryptionService>();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly SpotifyAuthService _authService;

    public SpotifyAuthServiceLocalSessionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new CantusDbContext(new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options);
        _dbContext.Database.EnsureCreated();

        _encryptionService.Encrypt(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        _encryptionService.Decrypt(Arg.Any<string>()).Returns(call => call.Arg<string>()["enc:".Length..]);

        _authService = new SpotifyAuthService(
            _dbContext,
            _encryptionService,
            Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" }),
            NullLogger<SpotifyAuthService>.Instance,
            timeProvider: _time);
    }

    public void Dispose()
    {
        SpotifyAuthService.ClearCache();
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetSessionByIdAsync_ForLocalSessionPastExpiry_ReturnsItWithoutRefreshOrDecrypt()
    {
        string id = await SeedLocalAsync(expiresAtUtc: DateTimeOffset.UtcNow.AddDays(-1));

        UserSession? session = await _authService.GetSessionByIdAsync(id);

        session.Should().NotBeNull();
        session!.Kind.Should().Be(SessionKind.Local);
        session.SpotifyUserId.Should().BeNull();
        session.AccessToken.Should().BeNull();
        session.RefreshToken.Should().BeNull();
        _encryptionService.DidNotReceive().Decrypt(Arg.Any<string>());
        UserSessionEntity row = await _dbContext.UserSessions.AsNoTracking().SingleAsync(u => u.Id == id);
        row.EncryptedAccessToken.Should().BeNull("a refresh would have written a token");
    }

    [Fact]
    public async Task GetSessionByIdAsync_ForCachedLocalSessionPastExpiry_ServesTheCacheWithoutReload()
    {
        string id = await SeedLocalAsync(expiresAtUtc: DateTimeOffset.UtcNow.AddDays(-1));
        (await _authService.GetSessionByIdAsync(id)).Should().NotBeNull();
        await _dbContext.UserSessions.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, "Renamed"));

        UserSession? session = await _authService.GetSessionByIdAsync(id);

        session!.DisplayName.Should().Be("Desktop", "an expired Local Session is still a cache hit");
    }

    [Fact]
    public async Task GetSessionByIdAsync_ForCachedLocalSessionDeletedBehindTheCache_StopsWorkingWithinAMinute()
    {
        // Stands in for a reader that loaded the row just before a revoke and cached it again
        // after the eviction: the row is gone but the cache still holds it.
        string id = await SeedLocalAsync();
        (await _authService.GetSessionByIdAsync(id)).Should().NotBeNull();
        await _dbContext.UserSessions.Where(u => u.Id == id).ExecuteDeleteAsync();

        _time.Advance(TimeSpan.FromSeconds(61));
        UserSession? session = await _authService.GetSessionByIdAsync(id);

        session.Should().BeNull("a Local cache entry is rechecked against the database once a minute");
    }

    [Fact]
    public async Task RefreshTokenAsync_ForLocalSession_Throws()
    {
        string id = await SeedLocalAsync();

        Func<Task> refresh = () => _authService.RefreshTokenAsync(id);

        await refresh.Should().ThrowAsync<InvalidOperationException>();
        _encryptionService.DidNotReceive().Decrypt(Arg.Any<string>());
    }

    [Fact]
    public async Task GetSessionForUserAsync_WithTwoLocalSessions_ReturnsEachOneWithoutCollision()
    {
        string first = await SeedLocalAsync("First desktop");
        string second = await SeedLocalAsync("Second desktop");

        UserSession? firstSession = await _authService.GetSessionForUserAsync(first);
        UserSession? secondSession = await _authService.GetSessionForUserAsync(second);
        UserSession? firstAgain = await _authService.GetSessionByIdAsync(first);

        firstSession!.DisplayName.Should().Be("First desktop");
        secondSession!.DisplayName.Should().Be("Second desktop");
        firstAgain!.Id.Should().Be(first);
    }

    [Fact]
    public async Task GetSessionForUserAsync_WithUnknownId_DoesNotMatchANullSpotifyUserId()
    {
        await SeedLocalAsync();

        UserSession? session = await _authService.GetSessionForUserAsync("unknown-" + Guid.NewGuid().ToString("N"));

        session.Should().BeNull();
    }

    [Fact]
    public async Task RevokeSessionAsync_ForLocalSession_DeletesItAndEvictsTheCache()
    {
        string id = await SeedLocalAsync();
        (await _authService.GetSessionByIdAsync(id)).Should().NotBeNull();

        bool revoked = await _authService.RevokeSessionAsync(id);

        revoked.Should().BeTrue();
        (await _authService.GetSessionByIdAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task GetAllSessionsAsync_WithLocalAndSpotifySessions_DecryptsOnlyTheSpotifyTokens()
    {
        string localId = await SeedLocalAsync();
        string spotifyId = await SeedSpotifyAsync();

        IReadOnlyList<UserSession> sessions = await _authService.GetAllSessionsAsync();

        sessions.Single(s => s.Id == localId).AccessToken.Should().BeNull();
        sessions.Single(s => s.Id == spotifyId).AccessToken.Should().Be("access");
        _encryptionService.Received(2).Decrypt(Arg.Any<string>());
    }

    private async Task<string> SeedLocalAsync(string displayName = "Desktop", DateTimeOffset? expiresAtUtc = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string id = "local_" + Guid.NewGuid().ToString("N");
        await _dbContext.UserSessions.AddAsync(new UserSessionEntity
        {
            Id = id,
            Kind = SessionKind.Local,
            DisplayName = displayName,
            ExpiresAtUtc = expiresAtUtc ?? DateTimeOffset.MaxValue,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return id;
    }

    private async Task<string> SeedSpotifyAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string id = "spotify_session_" + Guid.NewGuid().ToString("N");
        await _dbContext.UserSessions.AddAsync(new UserSessionEntity
        {
            Id = id,
            SpotifyUserId = "sp_" + Guid.NewGuid().ToString("N"),
            DisplayName = "Spotify user",
            EncryptedAccessToken = "enc:access",
            EncryptedRefreshToken = "enc:refresh",
            ExpiresAtUtc = now.AddHours(1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        return id;
    }
}
