using Cantus.Core.Models;
using Cantus.Infrastructure.Displays;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Security;
using Cantus.Infrastructure.Sessions;
using Cantus.Infrastructure.Spotify;
using Cantus.Infrastructure.Tests.Spotify;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Sessions;

[Collection(SpotifyAuthServiceCacheCollection.NAME)]
public sealed class LocalSessionServiceTests : IDisposable
{
    private const int IDLE_EXPIRY_DAYS = 90;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CantusDbContext> _dbOptions;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly LocalSessionOptions _options = new() { Enabled = true, IdleExpiryDays = IDLE_EXPIRY_DAYS };
    private readonly List<CantusDbContext> _contexts = new();

    public LocalSessionServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<CantusDbContext>().UseSqlite(_connection).Options;
        using CantusDbContext schema = new(_dbOptions);
        schema.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SpotifyAuthService.ClearCache();
        foreach (CantusDbContext context in _contexts)
        {
            context.Dispose();
        }

        _connection.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WithValidName_StoresALocalSessionWithoutSpotifyFields()
    {
        LocalSessionCreateResult result = await CreateService().CreateAsync("  jaret-desktop  ");

        result.Status.Should().Be(LocalSessionCreateStatus.Created);
        result.Session!.Kind.Should().Be(SessionKind.Local);
        result.Session.DisplayName.Should().Be("jaret-desktop");
        result.Session.SpotifyUserId.Should().BeNull();
        result.Session.AccessToken.Should().BeNull();

        UserSessionEntity row = await Context().UserSessions.SingleAsync(u => u.Id == result.Session.Id);
        row.Kind.Should().Be(SessionKind.Local);
        row.SpotifyUserId.Should().BeNull();
        row.EncryptedAccessToken.Should().BeNull();
        row.EncryptedRefreshToken.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithBlankName_IsRefused(string? name)
    {
        LocalSessionCreateResult result = await CreateService().CreateAsync(name);

        result.Status.Should().Be(LocalSessionCreateStatus.InvalidDisplayName);
        (await Context().UserSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WithNameOverDisplayNameLimit_IsRefused()
    {
        LocalSessionCreateResult result = await CreateService().CreateAsync(new string('a', DisplayInfo.MAX_NAME_LENGTH + 1));

        result.Status.Should().Be(LocalSessionCreateStatus.InvalidDisplayName);
    }

    [Fact]
    public async Task CreateAsync_AtTheCap_IsRefusedAndSpotifySessionsDoNotCount()
    {
        _options.MaxSessions = 1;
        await SeedAsync(SessionKind.Spotify, _time.GetUtcNow());
        (await CreateService().CreateAsync("first")).Status.Should().Be(LocalSessionCreateStatus.Created);

        LocalSessionCreateResult result = await CreateService().CreateAsync("second");

        result.Status.Should().Be(LocalSessionCreateStatus.LimitReached);
    }

    [Fact]
    public async Task CreateAsync_ConcurrentRequestsAtTheCap_CreateOnlyUpToTheCap()
    {
        const int REQUESTS = 8;
        // Each request needs its own connection to race, which a shared in-memory one cannot give.
        string databasePath = Path.Combine(Path.GetTempPath(), $"cantus-local-cap-{Guid.NewGuid():N}.db");
        DbContextOptions<CantusDbContext> fileOptions = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        _options.MaxSessions = 1;
        try
        {
            await using (CantusDbContext schema = new(fileOptions))
            {
                await schema.Database.EnsureCreatedAsync();
            }

            LocalSessionCreateResult[] results = await Task.WhenAll(Enumerable.Range(0, REQUESTS).Select(i => Task.Run(async () =>
            {
                await using CantusDbContext context = new(fileOptions);
                LocalSessionService service = new(context, _time, Options.Create(_options), NullLogger<LocalSessionService>.Instance);
                return await service.CreateAsync($"desktop-{i}");
            })));

            results.Count(r => r.Status == LocalSessionCreateStatus.Created).Should().Be(1);
            await using CantusDbContext check = new(fileOptions);
            (await check.UserSessions.CountAsync()).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CreateAsync_AtTheCap_PrunesIdleSessionsFirst()
    {
        _options.MaxSessions = 1;
        string idle = await SeedAsync(SessionKind.Local, _time.GetUtcNow().AddDays(-IDLE_EXPIRY_DAYS - 1));

        LocalSessionCreateResult result = await CreateService().CreateAsync("fresh");

        result.Status.Should().Be(LocalSessionCreateStatus.Created);
        result.Pruned.SessionIds.Should().Equal(idle);
    }

    [Fact]
    public async Task PruneIdleAsync_DeletesIdleLocalSessionsAndTheirDisplaysOnly()
    {
        DateTimeOffset now = _time.GetUtcNow();
        string idleLocal = await SeedAsync(SessionKind.Local, now.AddDays(-IDLE_EXPIRY_DAYS - 1));
        string recentLocal = await SeedAsync(SessionKind.Local, now.AddDays(-1));
        string oldSpotify = await SeedAsync(SessionKind.Spotify, now.AddDays(-IDLE_EXPIRY_DAYS - 10));
        CreatedDisplay idleDisplay = await CreateDisplayService().CreateAsync(idleLocal, "TV");
        CreatedDisplay recentDisplay = await CreateDisplayService().CreateAsync(recentLocal, "TV");

        LocalSessionPruneResult pruned = await CreateService().PruneIdleAsync();

        pruned.SessionIds.Should().Equal(idleLocal);
        pruned.DisplayIds.Should().Equal(idleDisplay.Display.Id);
        List<string> remaining = await Context().UserSessions.Select(u => u.Id).ToListAsync();
        remaining.Should().BeEquivalentTo([recentLocal, oldSpotify]);
        (await CreateDisplayService().ValidateAsync(idleDisplay.Token)).Should().BeNull();
        (await CreateDisplayService().ValidateAsync(recentDisplay.Token)).Should().NotBeNull();
    }

    [Fact]
    public async Task PruneIdleAsync_EvictsThePrunedSessionFromTheAuthCache()
    {
        string idle = await SeedAsync(SessionKind.Local, _time.GetUtcNow().AddDays(-IDLE_EXPIRY_DAYS - 1));
        SpotifyAuthService auth = CreateAuthService();
        (await auth.GetSessionByIdAsync(idle)).Should().NotBeNull();

        await CreateService().PruneIdleAsync();

        (await CreateAuthService().GetSessionByIdAsync(idle)).Should().BeNull();
    }

    [Fact]
    public async Task TouchAsync_KeepsAnActiveSessionFromBeingPruned()
    {
        string session = await SeedAsync(SessionKind.Local, _time.GetUtcNow());
        _time.Advance(TimeSpan.FromDays(IDLE_EXPIRY_DAYS - 1));
        await CreateService().TouchAsync(session);
        _time.Advance(TimeSpan.FromDays(2));

        LocalSessionPruneResult pruned = await CreateService().PruneIdleAsync();

        pruned.SessionIds.Should().BeEmpty();
        (await Context().UserSessions.AnyAsync(u => u.Id == session)).Should().BeTrue();
    }

    private LocalSessionService CreateService() =>
        new(Context(), _time, Options.Create(_options), NullLogger<LocalSessionService>.Instance);

    private DisplayService CreateDisplayService() =>
        new(Context(), _time, NullLogger<DisplayService>.Instance);

    private SpotifyAuthService CreateAuthService() =>
        new(
            Context(),
            new DataProtectionTokenEncryptionService(new EphemeralDataProtectionProvider()),
            Options.Create(new SpotifyOptions { ClientId = "client", ClientSecret = "secret" }),
            NullLogger<SpotifyAuthService>.Instance);

    private CantusDbContext Context()
    {
        CantusDbContext context = new(_dbOptions);
        _contexts.Add(context);
        return context;
    }

    private async Task<string> SeedAsync(SessionKind kind, DateTimeOffset updatedAtUtc)
    {
        string id = Guid.NewGuid().ToString("N");
        await using CantusDbContext context = new(_dbOptions);
        await context.UserSessions.AddAsync(new UserSessionEntity
        {
            Id = id,
            Kind = kind,
            SpotifyUserId = kind == SessionKind.Spotify ? "sp_" + id : null,
            DisplayName = "Seeded",
            EncryptedAccessToken = kind == SessionKind.Spotify ? "enc" : null,
            EncryptedRefreshToken = kind == SessionKind.Spotify ? "enc" : null,
            ExpiresAtUtc = DateTimeOffset.MaxValue,
            CreatedAtUtc = updatedAtUtc,
            UpdatedAtUtc = updatedAtUtc
        });
        await context.SaveChangesAsync();
        return id;
    }
}
