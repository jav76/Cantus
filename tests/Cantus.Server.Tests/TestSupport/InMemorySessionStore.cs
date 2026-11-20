using Cantus.Infrastructure.Displays;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Security;
using Cantus.Infrastructure.Spotify;
using Cantus.Server.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Tests.TestSupport;

/// <summary>
/// Backs a real <see cref="SpotifyAuthService"/> with an in-memory SQLite database so
/// tests can exercise session lookup at the HTTP and hub seams without mocking it.
/// Session and Spotify user ids are unique per seed because the service's session
/// cache is static and shared across tests.
/// </summary>
public sealed class InMemorySessionStore : IDisposable
{
    private const int SESSION_LIFETIME_HOURS = 1;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CantusDbContext> _dbOptions;
    private readonly ITokenEncryptionService _encryptionService;
    private readonly IOptions<SpotifyOptions> _options;
    private readonly List<CantusDbContext> _contexts = new();
    private readonly object _contextsLock = new();

    public InMemorySessionStore()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (CantusDbContext schemaContext = new(_dbOptions))
        {
            schemaContext.Database.EnsureCreated();
        }

        _encryptionService = new DataProtectionTokenEncryptionService(new EphemeralDataProtectionProvider());
        _options = Options.Create(new SpotifyOptions
        {
            ClientId = "test_client_id",
            ClientSecret = "test_client_secret",
        });
    }

    public SpotifyAuthService CreateAuthService()
    {
        CantusDbContext dbContext = new(_dbOptions);
        lock (_contextsLock)
        {
            _contexts.Add(dbContext);
        }

        return new SpotifyAuthService(
            dbContext,
            _encryptionService,
            _options,
            NullLogger<SpotifyAuthService>.Instance);
    }

    public DisplayService CreateDisplayService()
    {
        CantusDbContext dbContext = new(_dbOptions);
        lock (_contextsLock)
        {
            _contexts.Add(dbContext);
        }

        return new DisplayService(dbContext, TimeProvider.System, NullLogger<DisplayService>.Instance);
    }

    public CredentialResolver CreateCredentialResolver()
    {
        return new CredentialResolver(new SessionTokenResolver(), CreateAuthService(), CreateDisplayService());
    }

    public async Task<UserSessionEntity> SeedSessionAsync(string displayName)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UserSessionEntity entity = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SpotifyUserId = "sp_" + Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            EncryptedAccessToken = _encryptionService.Encrypt("access-token"),
            EncryptedRefreshToken = _encryptionService.Encrypt("refresh-token"),
            ExpiresAtUtc = now.AddHours(SESSION_LIFETIME_HOURS),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using CantusDbContext dbContext = new(_dbOptions);
        await dbContext.UserSessions.AddAsync(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> SessionExistsAsync(string sessionId)
    {
        await using CantusDbContext dbContext = new(_dbOptions);
        return await dbContext.UserSessions.AnyAsync(u => u.Id == sessionId);
    }

    public void Dispose()
    {
        lock (_contextsLock)
        {
            foreach (CantusDbContext dbContext in _contexts)
            {
                dbContext.Dispose();
            }

            _contexts.Clear();
        }

        _connection.Dispose();
    }
}
