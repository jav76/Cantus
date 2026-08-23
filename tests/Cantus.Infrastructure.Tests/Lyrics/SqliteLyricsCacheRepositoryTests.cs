using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public sealed class SqliteLyricsCacheRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CantusDbContext _dbContext;
    private readonly SqliteLyricsCacheRepository _repository;

    public SqliteLyricsCacheRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new CantusDbContext(options);
        _dbContext.Database.EnsureCreated();

        _repository = new SqliteLyricsCacheRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveLyricsAsync_ThenGetCachedLyricsAsync_ReturnsSavedLyrics()
    {
        var lyrics = new SyncedLyrics
        {
            TrackId = "track_123",
            Title = "Song Title",
            Artist = "Artist Name",
            Album = "Album Name",
            IsSynced = true,
            Lines =
            [
                new(TimeSpan.FromSeconds(5), "Line 1"),
                new(TimeSpan.FromSeconds(10), "Line 2")
            ]
        };

        await _repository.SaveLyricsAsync(lyrics);

        var retrieved = await _repository.GetCachedLyricsAsync("track_123");
        retrieved.Should().NotBeNull();
        retrieved!.TrackId.Should().Be("track_123");
        retrieved.Title.Should().Be("Song Title");
        retrieved.Artist.Should().Be("Artist Name");
        retrieved.Lines.Should().HaveCount(2);
        retrieved.Lines[0].Text.Should().Be("Line 1");
        retrieved.Lines[1].Text.Should().Be("Line 2");
    }

    [Fact]
    public async Task MarkNotFoundAsync_ThenIsMarkedNotFound_ReturnsTrue()
    {
        await _repository.MarkNotFoundAsync(
            "track_404",
            "Nonexistent Song",
            "Unknown Artist",
            "Album",
            180000,
            TimeSpan.FromDays(7));

        bool isNotFound = await _repository.IsMarkedNotFoundAsync("track_404");
        isNotFound.Should().BeTrue();

        var lyrics = await _repository.GetCachedLyricsAsync("track_404");
        lyrics.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedLyricsAsync_WhenExpired_ReturnsNullAndCleansUp()
    {
        var lyrics = new SyncedLyrics
        {
            TrackId = "track_expiring",
            Title = "Expiring",
            Artist = "Artist",
            Lines = [new(TimeSpan.FromSeconds(1), "Hello")]
        };

        // Save with negative TTL so it is already expired
        await _repository.SaveLyricsAsync(lyrics, timeToLive: TimeSpan.FromSeconds(-10));

        var retrieved = await _repository.GetCachedLyricsAsync("track_expiring");
        retrieved.Should().BeNull();
    }
}
