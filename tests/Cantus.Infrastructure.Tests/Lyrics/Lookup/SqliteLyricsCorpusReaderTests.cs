using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics.Lookup;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics.Lookup;

public sealed class SqliteLyricsCorpusReaderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly SqliteConnection _connection;
    private readonly CantusDbContext _dbContext;
    private readonly SqliteLyricsCorpusReader _reader;

    public SqliteLyricsCorpusReaderTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<CantusDbContext> options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new CantusDbContext(options);
        _dbContext.Database.EnsureCreated();
        _reader = new SqliteLyricsCorpusReader(_dbContext, _timeProvider);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private static CachedLyricsEntity CacheRow(string trackId, string? plainLyrics = "cached words") => new()
    {
        TrackId = trackId,
        TrackName = $"Cached {trackId}",
        ArtistName = "Cached Artist",
        AlbumName = "Cached Album",
        PlainLyrics = plainLyrics,
    };

    private static LyricsOverrideEntity OverrideRow(string trackId, string? plainLyrics = "override words") => new()
    {
        TrackId = trackId,
        Kind = LyricsOverrideKind.Candidate,
        Provider = "LRCLIB",
        ProviderCandidateId = "1",
        TrackName = $"Override {trackId}",
        ArtistName = "Override Artist",
        AlbumName = "Override Album",
        PlainLyrics = plainLyrics,
    };

    private async Task SeedAsync(params object[] entities)
    {
        _dbContext.AddRange(entities);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsOnlyPositiveUnexpiredCacheRows()
    {
        CachedLyricsEntity notFound = CacheRow("not-found", null);
        notFound.IsNotFound = true;
        CachedLyricsEntity instrumental = CacheRow("instrumental", null);
        instrumental.IsInstrumental = true;
        CachedLyricsEntity expired = CacheRow("expired");
        expired.ExpiresAtUtc = Now.AddMinutes(-1);
        CachedLyricsEntity live = CacheRow("live");
        live.ExpiresAtUtc = Now.AddMinutes(1);
        await SeedAsync(CacheRow("positive"), notFound, instrumental, CacheRow("no-text", null), expired, live);

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAllAsync();

        entries.Select(e => e.TrackId).Should().BeEquivalentTo("positive", "live");
        LyricsCorpusEntry positive = entries.Single(e => e.TrackId == "positive");
        positive.Should().Be(new LyricsCorpusEntry(
            "positive",
            "Cached positive",
            "Cached Artist",
            "Cached Album",
            "cached words",
            null));
    }

    [Fact]
    public async Task ReadAllAsync_WhenTheClockPassesTheExpiry_DropsTheRow()
    {
        CachedLyricsEntity row = CacheRow("ttl");
        row.ExpiresAtUtc = Now.AddHours(1);
        await SeedAsync(row);

        (await _reader.ReadAllAsync()).Should().ContainSingle();
        _timeProvider.Advance(TimeSpan.FromHours(1));

        (await _reader.ReadAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAllAsync_CandidateOverride_ShadowsTheCacheRow()
    {
        await SeedAsync(CacheRow("t1"), OverrideRow("t1"));

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAllAsync();

        entries.Should().ContainSingle().Which.Should().Be(new LyricsCorpusEntry(
            "t1",
            "Override t1",
            "Override Artist",
            "Override Album",
            "override words",
            null));
    }

    [Fact]
    public async Task ReadAllAsync_OverrideShadowsAnExpiredCacheRowToo()
    {
        CachedLyricsEntity expired = CacheRow("t1");
        expired.ExpiresAtUtc = Now.AddDays(-1);
        await SeedAsync(expired, OverrideRow("t1"));

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAllAsync();

        entries.Should().ContainSingle().Which.PlainLyrics.Should().Be("override words");
    }

    [Fact]
    public async Task ReadAllAsync_NoLyricsOverride_HidesTheCachedTrack()
    {
        LyricsOverrideEntity noLyrics = new() { TrackId = "t1", Kind = LyricsOverrideKind.NoLyrics };
        await SeedAsync(CacheRow("t1"), noLyrics, CacheRow("t2"));

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAllAsync();

        entries.Select(e => e.TrackId).Should().Equal("t2");
    }

    [Fact]
    public async Task ReadAllAsync_InstrumentalOrTextlessOverride_HidesTheCachedTrack()
    {
        LyricsOverrideEntity instrumental = OverrideRow("instrumental", null);
        instrumental.IsInstrumental = true;
        await SeedAsync(
            CacheRow("instrumental"),
            instrumental,
            CacheRow("textless"),
            OverrideRow("textless", null));

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAllAsync();

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAllAsync_OverrideWithoutNames_FallsBackToTheCacheNames()
    {
        LyricsOverrideEntity unnamed = OverrideRow("t1");
        unnamed.TrackName = null;
        unnamed.ArtistName = null;
        unnamed.AlbumName = null;
        await SeedAsync(CacheRow("t1"), unnamed);

        LyricsCorpusEntry entry = (await _reader.ReadAllAsync()).Single();

        entry.Title.Should().Be("Cached t1");
        entry.Artist.Should().Be("Cached Artist");
        entry.Album.Should().Be("Cached Album");
        entry.PlainLyrics.Should().Be("override words");
    }

    [Fact]
    public async Task ReadAllAsync_OverrideWithoutNames_FallsBackToANegativeCacheRowsNames()
    {
        CachedLyricsEntity notFound = CacheRow("t1", null);
        notFound.IsNotFound = true;
        LyricsOverrideEntity unnamed = OverrideRow("t1");
        unnamed.TrackName = null;
        unnamed.ArtistName = null;
        await SeedAsync(notFound, unnamed);

        LyricsCorpusEntry entry = (await _reader.ReadAllAsync()).Single();

        entry.Title.Should().Be("Cached t1");
        entry.Artist.Should().Be("Cached Artist");
    }

    [Fact]
    public async Task ReadAllAsync_OverrideWithoutACacheRow_IsIncluded()
    {
        await SeedAsync(OverrideRow("only-override"));

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAllAsync();

        entries.Should().ContainSingle().Which.TrackId.Should().Be("only-override");
    }

    [Fact]
    public async Task ReadAsync_ReturnsOnlyTheRequestedTracksWithTheSameRules()
    {
        LyricsOverrideEntity noLyrics = new() { TrackId = "hidden", Kind = LyricsOverrideKind.NoLyrics };
        await SeedAsync(
            CacheRow("t1"),
            CacheRow("t2"),
            OverrideRow("t2"),
            CacheRow("hidden"),
            noLyrics,
            CacheRow("not-requested"));

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAsync(["t1", "t2", "hidden", "missing"]);

        entries.Select(e => (e.TrackId, e.PlainLyrics)).Should().BeEquivalentTo(new[]
        {
            ("t1", "cached words"),
            ("t2", "override words"),
        });
    }

    [Fact]
    public async Task ReadAsync_WithNoIds_ReturnsNothing()
    {
        await SeedAsync(CacheRow("t1"));

        IReadOnlyList<LyricsCorpusEntry> entries = await _reader.ReadAsync([]);

        entries.Should().BeEmpty();
    }
}
