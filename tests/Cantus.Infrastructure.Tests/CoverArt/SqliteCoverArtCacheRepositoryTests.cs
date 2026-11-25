using Cantus.Infrastructure.CoverArt;
using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Cantus.Infrastructure.Tests.CoverArt;

public sealed class SqliteCoverArtCacheRepositoryTests : IDisposable
{
    private const string TRACK_ID = "local:0123456789abcdef0123456789abcdef";
    private const string ART_URL = "https://coverartarchive.org/release-group/9162580e-5df4-32de-80cc-f45a8d8a9b1d/front-500";

    private static readonly TimeSpan TimeToLive = TimeSpan.FromDays(30);

    private readonly SqliteConnection _connection;
    private readonly CantusDbContext _dbContext;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly SqliteCoverArtCacheRepository _repository;

    public SqliteCoverArtCacheRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<CantusDbContext> options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new CantusDbContext(options);
        _dbContext.Database.EnsureCreated();

        _repository = new SqliteCoverArtCacheRepository(_dbContext, _timeProvider);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetAsync_ForAnUnknownTrack_ReturnsNull()
    {
        (await _repository.GetAsync(TRACK_ID)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsTheCover()
    {
        await _repository.SaveAsync(TRACK_ID, ART_URL, TimeToLive);

        (await _repository.GetAsync(TRACK_ID)).Should().Be(new CoverArtCacheEntry(ART_URL));
    }

    [Fact]
    public async Task SaveAsync_WithNoUrl_RemembersTheMiss()
    {
        await _repository.SaveAsync(TRACK_ID, null, TimeToLive);

        (await _repository.GetAsync(TRACK_ID)).Should().Be(new CoverArtCacheEntry(null));
    }

    [Fact]
    public async Task GetAsync_OnceTheEntryExpires_ReturnsNull()
    {
        await _repository.SaveAsync(TRACK_ID, ART_URL, TimeToLive);

        _timeProvider.Advance(TimeToLive - TimeSpan.FromSeconds(1));
        (await _repository.GetAsync(TRACK_ID)).Should().NotBeNull();

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await _repository.GetAsync(TRACK_ID)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_OverAnExpiredMiss_ReplacesItAndRestartsTheLifetime()
    {
        await _repository.SaveAsync(TRACK_ID, null, TimeToLive);
        _timeProvider.Advance(TimeToLive);

        await _repository.SaveAsync(TRACK_ID, ART_URL, TimeToLive);

        (await _repository.GetAsync(TRACK_ID)).Should().Be(new CoverArtCacheEntry(ART_URL));
        (await _dbContext.CachedCoverArt.CountAsync()).Should().Be(1);
    }
}
