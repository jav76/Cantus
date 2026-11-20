using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

/// <summary>
/// Builds the schema from the real migrations (not EnsureCreated) so these
/// tests prove the schema change ships. A fresh context on the same database
/// stands in for a server restart.
/// </summary>
public sealed class LyricsOverridePersistenceTests : IDisposable
{
    private const string TRACK_ID = "track-override";

    private readonly SqliteConnection _connection;

    public LyricsOverridePersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using CantusDbContext migrationContext = CreateContext();
        migrationContext.Database.Migrate();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private CantusDbContext CreateContext()
    {
        DbContextOptions<CantusDbContext> options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new CantusDbContext(options);
    }

    private static SyncedLyrics ChosenLyrics() => new()
    {
        TrackId = TRACK_ID,
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Album = "The Desired Effect",
        IsSynced = true,
        Lines =
        [
            new LyricLine(TimeSpan.FromSeconds(1), "I'm writing you this letter"),
            new LyricLine(TimeSpan.FromSeconds(4), "If you ain't figured out")
        ]
    };

    [Fact]
    public async Task CandidateOverride_SurvivesARestart()
    {
        using (CantusDbContext context = CreateContext())
        {
            SqliteLyricsCacheRepository repository = new(context, TimeProvider.System);
            await repository.SaveLyricsOverrideAsync(LyricsOverride.ForCandidate("LRCLIB", "501", ChosenLyrics()));
        }

        using CantusDbContext restarted = CreateContext();
        LyricsOverride? stored = await new SqliteLyricsCacheRepository(restarted, TimeProvider.System).GetLyricsOverrideAsync(TRACK_ID);

        stored.Should().NotBeNull();
        stored!.Kind.Should().Be(LyricsOverrideKind.Candidate);
        stored.Provider.Should().Be("LRCLIB");
        stored.ProviderCandidateId.Should().Be("501");
        stored.Lyrics.Should().NotBeNull();
        stored.Lyrics!.TrackId.Should().Be(TRACK_ID);
        stored.Lyrics.Album.Should().Be("The Desired Effect");
        stored.Lyrics.IsSynced.Should().BeTrue();
        stored.Lyrics.Lines.Select(l => l.Text)
            .Should().Equal("I'm writing you this letter", "If you ain't figured out");
    }

    [Fact]
    public async Task PlainCandidateOverride_KeepsThePlainLyrics()
    {
        SyncedLyrics plain = new()
        {
            TrackId = TRACK_ID,
            Title = "Miss America",
            Artist = "Brandon Flowers",
            IsSynced = false,
            Lines = [],
            PlainLyrics = "I'm writing you this letter"
        };

        using (CantusDbContext context = CreateContext())
        {
            await new SqliteLyricsCacheRepository(context, TimeProvider.System)
                .SaveLyricsOverrideAsync(LyricsOverride.ForCandidate("LRCLIB", "502", plain));
        }

        using CantusDbContext restarted = CreateContext();
        LyricsOverride? stored = await new SqliteLyricsCacheRepository(restarted, TimeProvider.System).GetLyricsOverrideAsync(TRACK_ID);

        stored!.Lyrics!.IsSynced.Should().BeFalse();
        stored.Lyrics.Lines.Should().BeEmpty();
        stored.Lyrics.PlainLyrics.Should().Be("I'm writing you this letter");
    }

    [Fact]
    public async Task NoLyricsOverride_SurvivesARestart()
    {
        using (CantusDbContext context = CreateContext())
        {
            await new SqliteLyricsCacheRepository(context, TimeProvider.System)
                .SaveLyricsOverrideAsync(LyricsOverride.ForNoLyrics(TRACK_ID));
        }

        using CantusDbContext restarted = CreateContext();
        LyricsOverride? stored = await new SqliteLyricsCacheRepository(restarted, TimeProvider.System).GetLyricsOverrideAsync(TRACK_ID);

        stored.Should().NotBeNull();
        stored!.Kind.Should().Be(LyricsOverrideKind.NoLyrics);
        stored.Lyrics.Should().BeNull();
        stored.Provider.Should().BeNull();
    }

    [Fact]
    public async Task Override_IsNotUndoneByCacheExpiryOrNegativeCaching()
    {
        using CantusDbContext context = CreateContext();
        SqliteLyricsCacheRepository repository = new(context, TimeProvider.System);
        await repository.SaveLyricsOverrideAsync(LyricsOverride.ForCandidate("NetEase", "111", ChosenLyrics()));

        await repository.SaveLyricsAsync(ChosenLyrics(), timeToLive: TimeSpan.FromSeconds(-10));
        await repository.MarkNotFoundAsync(
            TRACK_ID,
            "Miss America",
            "Brandon Flowers",
            string.Empty,
            0,
            TimeSpan.FromDays(30));

        LyricsOverride? stored = await repository.GetLyricsOverrideAsync(TRACK_ID);
        stored.Should().NotBeNull();
        stored!.ProviderCandidateId.Should().Be("111");
    }

    [Fact]
    public async Task SaveLyricsOverride_ReplacesThePreviousOverride()
    {
        using CantusDbContext context = CreateContext();
        SqliteLyricsCacheRepository repository = new(context, TimeProvider.System);
        await repository.SaveLyricsOverrideAsync(LyricsOverride.ForCandidate("LRCLIB", "501", ChosenLyrics()));

        await repository.SaveLyricsOverrideAsync(LyricsOverride.ForNoLyrics(TRACK_ID));

        LyricsOverride? stored = await repository.GetLyricsOverrideAsync(TRACK_ID);
        stored!.Kind.Should().Be(LyricsOverrideKind.NoLyrics);
        stored.Provider.Should().BeNull();
        stored.Lyrics.Should().BeNull();
    }

    [Fact]
    public async Task RemoveLyricsOverride_ReturnsTheTrackToAutomaticMatching()
    {
        using CantusDbContext context = CreateContext();
        SqliteLyricsCacheRepository repository = new(context, TimeProvider.System);
        await repository.SaveLyricsOverrideAsync(LyricsOverride.ForNoLyrics(TRACK_ID));

        await repository.RemoveLyricsOverrideAsync(TRACK_ID);

        (await repository.GetLyricsOverrideAsync(TRACK_ID)).Should().BeNull();
    }

    [Fact]
    public async Task WordTimedCandidateOverride_KeepsItsSyllablesAcrossARestart()
    {
        SyncedLyrics wordTimed = new()
        {
            TrackId = TRACK_ID,
            Title = "Miss America",
            Artist = "Brandon Flowers",
            IsSynced = true,
            Lines =
            [
                new LyricLine(
                    TimeSpan.FromMilliseconds(27360),
                    "zulu alpha",
                    [
                        new LyricSyllable(TimeSpan.FromMilliseconds(27360), TimeSpan.FromMilliseconds(245), "zulu "),
                        new LyricSyllable(TimeSpan.FromMilliseconds(27605), TimeSpan.FromMilliseconds(90), "alpha")
                    ]),
                new LyricLine(
                    TimeSpan.FromMilliseconds(40000),
                    "甲乙",
                    [
                        new LyricSyllable(TimeSpan.FromMilliseconds(40000), TimeSpan.FromMilliseconds(890), "甲"),
                        new LyricSyllable(TimeSpan.FromMilliseconds(40890), TimeSpan.FromMilliseconds(380), "乙")
                    ])
            ]
        };

        using (CantusDbContext context = CreateContext())
        {
            await new SqliteLyricsCacheRepository(context, TimeProvider.System)
                .SaveLyricsOverrideAsync(LyricsOverride.ForCandidate("NetEase", "111", wordTimed));
        }

        using CantusDbContext restarted = CreateContext();
        LyricsOverride? stored = await new SqliteLyricsCacheRepository(restarted, TimeProvider.System)
            .GetLyricsOverrideAsync(TRACK_ID);

        SyncedLyrics lyrics = stored!.Lyrics!;
        lyrics.HasWordTiming.Should().BeTrue();
        lyrics.Lines.Select(l => l.Text).Should().Equal("zulu alpha", "甲乙");
        lyrics.Lines[0].Syllables.Should().Equal(wordTimed.Lines[0].Syllables);
        lyrics.Lines[1].Syllables.Should().Equal(wordTimed.Lines[1].Syllables);
    }

    [Fact]
    public async Task LineLevelOverride_ReplacingAWordTimedOne_DropsTheSyllables()
    {
        SyncedLyrics wordTimed = new()
        {
            TrackId = TRACK_ID,
            Title = "Miss America",
            Artist = "Brandon Flowers",
            IsSynced = true,
            Lines =
            [
                new LyricLine(
                    TimeSpan.FromSeconds(1),
                    "word",
                    [new LyricSyllable(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(300), "word")])
            ]
        };

        using CantusDbContext context = CreateContext();
        SqliteLyricsCacheRepository repository = new(context, TimeProvider.System);
        await repository.SaveLyricsOverrideAsync(LyricsOverride.ForCandidate("NetEase", "111", wordTimed));
        await repository.SaveLyricsOverrideAsync(LyricsOverride.ForCandidate("LRCLIB", "501", ChosenLyrics()));

        LyricsOverride? stored = await repository.GetLyricsOverrideAsync(TRACK_ID);

        stored!.Lyrics!.HasWordTiming.Should().BeFalse();
        stored.Lyrics.Lines.Select(l => l.Text)
            .Should().Equal("I'm writing you this letter", "If you ain't figured out");
    }
}
