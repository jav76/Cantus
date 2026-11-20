using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public sealed class SqliteLyricsCacheRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CantusDbContext _dbContext;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly SqliteLyricsCacheRepository _repository;

    public SqliteLyricsCacheRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<CantusDbContext> options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new CantusDbContext(options);
        _dbContext.Database.EnsureCreated();

        _repository = new SqliteLyricsCacheRepository(_dbContext, _timeProvider);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveLyricsAsync_ThenGetCachedLyricsAsync_ReturnsSavedLyrics()
    {
        SyncedLyrics lyrics = new()
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

        SyncedLyrics? retrieved = await _repository.GetCachedLyricsAsync("track_123");
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

        SyncedLyrics? lyrics = await _repository.GetCachedLyricsAsync("track_404");
        lyrics.Should().BeNull();
    }

    [Fact]
    public async Task SaveLyricsAsync_OverANotFoundMark_ReplacesItWithTheLyrics()
    {
        await _repository.MarkNotFoundAsync(
            "track_refetched",
            "Song",
            "Artist",
            "Album",
            180000,
            TimeSpan.FromDays(7));

        await _repository.SaveLyricsAsync(new SyncedLyrics
        {
            TrackId = "track_refetched",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = [new(TimeSpan.FromSeconds(1), "Found now")]
        });

        (await _repository.IsMarkedNotFoundAsync("track_refetched")).Should().BeFalse();
        SyncedLyrics? retrieved = await _repository.GetCachedLyricsAsync("track_refetched");
        retrieved!.Lines.Select(l => l.Text).Should().Equal("Found now");
    }

    [Fact]
    public async Task GetCachedLyricsAsync_WhenExpired_ReturnsNullAndCleansUp()
    {
        SyncedLyrics lyrics = new()
        {
            TrackId = "track_expiring",
            Title = "Expiring",
            Artist = "Artist",
            Lines = [new(TimeSpan.FromSeconds(1), "Hello")]
        };

        // Save with negative TTL so it is already expired
        await _repository.SaveLyricsAsync(lyrics, timeToLive: TimeSpan.FromSeconds(-10));

        SyncedLyrics? retrieved = await _repository.GetCachedLyricsAsync("track_expiring");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task SaveLyricsAsync_WithSyllables_PreservesSyllableTimingOnRoundtrip()
    {
        LyricSyllable syl1 = new(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500), "Hello");
        LyricSyllable syl2 = new(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(500), "World");
        LyricLine line = new(TimeSpan.FromSeconds(1), "Hello World", [syl1, syl2]);

        SyncedLyrics lyrics = new()
        {
            TrackId = "track_syllables",
            Title = "Syllable Song",
            Artist = "Syllable Artist",
            IsSynced = true,
            Lines = [line]
        };

        await _repository.SaveLyricsAsync(lyrics);

        SyncedLyrics? retrieved = await _repository.GetCachedLyricsAsync("track_syllables");
        retrieved.Should().NotBeNull();
        retrieved!.Lines.Should().HaveCount(1);
        retrieved.Lines[0].Text.Should().Be("Hello World");
        retrieved.Lines[0].Syllables.Should().NotBeNull();
        retrieved.Lines[0].Syllables!.Should().HaveCount(2);
        retrieved.Lines[0].Syllables![0].Text.Should().Be("Hello");
        retrieved.Lines[0].Syllables![1].Text.Should().Be("World");
    }

    [Fact]
    public async Task SaveLyricsAsync_WithWordTiming_RoundTripsSyllablesExactly()
    {
        LyricLine latin = new(
            TimeSpan.FromMilliseconds(32820),
            "echofox golf, hotel",
            [
                new(TimeSpan.FromMilliseconds(32820), TimeSpan.FromMilliseconds(540), "echo"),
                new(TimeSpan.FromMilliseconds(33360), TimeSpan.FromMilliseconds(120), "fox "),
                new(TimeSpan.FromMilliseconds(33480), TimeSpan.FromMilliseconds(180), "golf"),
                new(TimeSpan.FromMilliseconds(33660), TimeSpan.Zero, ", "),
                new(TimeSpan.FromMilliseconds(33660), TimeSpan.FromMilliseconds(243), "hotel")
            ]);
        LyricLine cjk = new(
            TimeSpan.FromMilliseconds(40001),
            "甲乙",
            [
                new(TimeSpan.FromMilliseconds(40001), TimeSpan.FromMilliseconds(889), "甲"),
                new(TimeSpan.FromMilliseconds(40890), TimeSpan.FromMilliseconds(381), "乙")
            ]);
        LyricLine lineOnly = new(TimeSpan.FromMilliseconds(45007), "no word timing here");

        SyncedLyrics lyrics = new()
        {
            TrackId = "track_word_timed",
            Title = "Word Timed",
            Artist = "Artist",
            IsSynced = true,
            Lines = [latin, cjk, lineOnly],
            PlainLyrics = "echofox golf, hotel\n甲乙\nno word timing here"
        };

        await _repository.SaveLyricsAsync(lyrics);
        _dbContext.ChangeTracker.Clear();

        SyncedLyrics? retrieved = await _repository.GetCachedLyricsAsync("track_word_timed");

        retrieved.Should().NotBeNull();
        retrieved!.IsSynced.Should().BeTrue();
        retrieved.HasWordTiming.Should().BeTrue();
        retrieved.PlainLyrics.Should().Be(lyrics.PlainLyrics);
        retrieved.Lines.Should().HaveCount(3);
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            retrieved.Lines[i].Timestamp.Should().Be(lyrics.Lines[i].Timestamp);
            retrieved.Lines[i].Text.Should().Be(lyrics.Lines[i].Text);
        }

        retrieved.Lines[0].Syllables.Should().Equal(latin.Syllables);
        retrieved.Lines[1].Syllables.Should().Equal(cjk.Syllables);
        retrieved.Lines[2].Syllables.Should().BeNull();
    }

    [Fact]
    public async Task TryStartWordTimingUpgradeAsync_WhenNoEntryExists_ReturnsFalse()
    {
        bool started = await _repository.TryStartWordTimingUpgradeAsync("missing", 3, TimeSpan.FromDays(7));

        started.Should().BeFalse();
    }

    [Fact]
    public async Task TryStartWordTimingUpgradeAsync_WhenEntryIsNegativeCached_ReturnsFalse()
    {
        await _repository.MarkNotFoundAsync("track_404", "Song", "Artist", "Album", 1000, TimeSpan.FromDays(30));
        _timeProvider.Advance(TimeSpan.FromDays(8));

        bool started = await _repository.TryStartWordTimingUpgradeAsync("track_404", 3, TimeSpan.FromDays(7));

        started.Should().BeFalse();
    }

    [Fact]
    public async Task TryStartWordTimingUpgradeAsync_WithinIntervalOfFetch_ReturnsFalse()
    {
        await _repository.SaveLyricsAsync(LineOnlyLyrics("track_fresh"));
        _timeProvider.Advance(TimeSpan.FromDays(6));

        bool started = await _repository.TryStartWordTimingUpgradeAsync("track_fresh", 3, TimeSpan.FromDays(7));

        started.Should().BeFalse();
    }

    [Fact]
    public async Task TryStartWordTimingUpgradeAsync_AfterInterval_AllowsOneAttemptPerInterval()
    {
        await _repository.SaveLyricsAsync(LineOnlyLyrics("track_line"));
        _timeProvider.Advance(TimeSpan.FromDays(7));

        bool first = await _repository.TryStartWordTimingUpgradeAsync("track_line", 3, TimeSpan.FromDays(7));
        bool secondSamePlay = await _repository.TryStartWordTimingUpgradeAsync("track_line", 3, TimeSpan.FromDays(7));
        _timeProvider.Advance(TimeSpan.FromDays(7));
        bool nextInterval = await _repository.TryStartWordTimingUpgradeAsync("track_line", 3, TimeSpan.FromDays(7));

        first.Should().BeTrue();
        secondSamePlay.Should().BeFalse();
        nextInterval.Should().BeTrue();
    }

    [Fact]
    public async Task TryStartWordTimingUpgradeAsync_AfterMaxAttempts_StopsRetrying()
    {
        await _repository.SaveLyricsAsync(LineOnlyLyrics("track_bounded"));
        List<bool> results = new();

        for (int i = 0; i < 5; i++)
        {
            _timeProvider.Advance(TimeSpan.FromDays(7));
            results.Add(await _repository.TryStartWordTimingUpgradeAsync("track_bounded", 2, TimeSpan.FromDays(7)));
        }

        results.Should().Equal(true, true, false, false, false);
    }

    [Fact]
    public async Task SaveLyricsAsync_AfterUpgradeAttempts_ResetsTheRetryBudget()
    {
        await _repository.SaveLyricsAsync(LineOnlyLyrics("track_refetched"));
        _timeProvider.Advance(TimeSpan.FromDays(7));
        (await _repository.TryStartWordTimingUpgradeAsync("track_refetched", 1, TimeSpan.FromDays(7)))
            .Should().BeTrue();

        await _repository.SaveLyricsAsync(LineOnlyLyrics("track_refetched"));
        _timeProvider.Advance(TimeSpan.FromDays(7));

        bool started = await _repository.TryStartWordTimingUpgradeAsync("track_refetched", 1, TimeSpan.FromDays(7));

        started.Should().BeTrue();
    }

    private static SyncedLyrics LineOnlyLyrics(string trackId) => new()
    {
        TrackId = trackId,
        Title = "Song",
        Artist = "Artist",
        IsSynced = true,
        Lines = [new(TimeSpan.FromSeconds(1), "Line only")]
    };
}
