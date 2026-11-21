using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Lyrics.Lookup;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics.Lookup;

/// <summary>
/// Drives the index through the real repository writes with the change interceptor
/// attached, one context per operation as one DI scope per request would.
/// </summary>
public sealed class LyricsCorpusIndexSyncTests : IDisposable
{
    private const string TRACK_ID = "track-sync";
    private const string CACHED_LYRICS = "the river runs down to the sea tonight\nunder a silver moon we sing";
    private const string CACHED_FRAGMENT = "the river runs down to the sea";
    private const string OVERRIDE_LYRICS = "golden bells ring out across the frozen hills\nand nobody hears them";
    private const string OVERRIDE_FRAGMENT = "golden bells ring out across the frozen hills";
    private static readonly TimeSpan NotFoundTimeToLive = TimeSpan.FromDays(7);

    private readonly SqliteConnection _connection;
    private readonly LyricsSearchIndex _index;
    private readonly LyricsCorpusChangeInterceptor _interceptor;

    public LyricsCorpusIndexSyncTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _index = new LyricsSearchIndex(TimeProvider.System, NullLogger<LyricsSearchIndex>.Instance);
        _interceptor = new LyricsCorpusChangeInterceptor(_index);

        using CantusDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _index.Dispose();
        _connection.Dispose();
    }

    private CantusDbContext CreateContext()
    {
        DbContextOptions<CantusDbContext> options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;

        return new CantusDbContext(options);
    }

    /// <summary>The one place these tests build a repository, so a constructor change is a one-line fix.</summary>
    private static SqliteLyricsCacheRepository CreateRepository(CantusDbContext context) => new(context, TimeProvider.System);

    private async Task WithRepositoryAsync(Func<SqliteLyricsCacheRepository, Task> action)
    {
        using CantusDbContext context = CreateContext();
        await action(CreateRepository(context));
    }

    private async Task<LyricsIndexSnapshot> GetSnapshotAsync()
    {
        using CantusDbContext context = CreateContext();
        return await _index.GetSnapshotAsync(new SqliteLyricsCorpusReader(context, TimeProvider.System));
    }

    private SongCandidate? TopMatch(LyricsIndexSnapshot snapshot, string fragment) =>
        _index.Scan(snapshot, LyricTextNormalizer.PrepareFragment(fragment)).Candidates.MaxBy(c => c.Score);

    private static SyncedLyrics Lyrics(string plainLyrics, string title = "River Song") => new()
    {
        TrackId = TRACK_ID,
        Title = title,
        Artist = "The Band",
        Album = "Album",
        PlainLyrics = plainLyrics,
    };

    private static LyricsOverride CandidateOverride() =>
        LyricsOverride.ForCandidate("LRCLIB", "77", Lyrics(OVERRIDE_LYRICS, "Golden Bells"));

    [Fact]
    public async Task SaveLyrics_AfterTheIndexWasBuilt_AddsTheTrackOnTheNextLookup()
    {
        LyricsIndexSnapshot empty = await GetSnapshotAsync();
        empty.Count.Should().Be(0);

        await WithRepositoryAsync(repository => repository.SaveLyricsAsync(Lyrics(CACHED_LYRICS)));
        LyricsIndexSnapshot refreshed = await GetSnapshotAsync();

        refreshed.Should().NotBeSameAs(empty);
        TopMatch(refreshed, CACHED_FRAGMENT)!.SpotifyTrackId.Should().Be(TRACK_ID);
    }

    [Fact]
    public async Task MarkNotFound_OverCachedLyrics_RemovesTheTrack()
    {
        await WithRepositoryAsync(repository => repository.SaveLyricsAsync(Lyrics(CACHED_LYRICS)));
        (await GetSnapshotAsync()).Count.Should().Be(1);

        await WithRepositoryAsync(repository => repository.MarkNotFoundAsync(
            TRACK_ID,
            "River Song",
            "The Band",
            "Album",
            0,
            NotFoundTimeToLive));
        LyricsIndexSnapshot refreshed = await GetSnapshotAsync();

        refreshed.Count.Should().Be(0);
    }

    [Fact]
    public async Task SaveOverride_ReplacesTheCachedLyricsInTheIndex()
    {
        await WithRepositoryAsync(repository => repository.SaveLyricsAsync(Lyrics(CACHED_LYRICS)));
        await GetSnapshotAsync();

        await WithRepositoryAsync(repository => repository.SaveLyricsOverrideAsync(CandidateOverride()));
        LyricsIndexSnapshot refreshed = await GetSnapshotAsync();

        refreshed.Songs.Should().ContainSingle().Which.Title.Should().Be("Golden Bells");
        TopMatch(refreshed, OVERRIDE_FRAGMENT)!.SpotifyTrackId.Should().Be(TRACK_ID);
        TopMatch(refreshed, CACHED_FRAGMENT).Should().BeNull();
    }

    [Fact]
    public async Task SaveNoLyricsOverride_HidesTheCachedTrack()
    {
        await WithRepositoryAsync(repository => repository.SaveLyricsAsync(Lyrics(CACHED_LYRICS)));
        await GetSnapshotAsync();

        await WithRepositoryAsync(repository =>
            repository.SaveLyricsOverrideAsync(LyricsOverride.ForNoLyrics(TRACK_ID)));
        LyricsIndexSnapshot refreshed = await GetSnapshotAsync();

        refreshed.Count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveOverride_RestoresTheCachedLyrics()
    {
        await WithRepositoryAsync(repository => repository.SaveLyricsAsync(Lyrics(CACHED_LYRICS)));
        await WithRepositoryAsync(repository => repository.SaveLyricsOverrideAsync(CandidateOverride()));
        LyricsIndexSnapshot overridden = await GetSnapshotAsync();
        TopMatch(overridden, OVERRIDE_FRAGMENT)!.SpotifyTrackId.Should().Be(TRACK_ID);

        await WithRepositoryAsync(repository => repository.RemoveLyricsOverrideAsync(TRACK_ID));
        LyricsIndexSnapshot restored = await GetSnapshotAsync();

        restored.Should().NotBeSameAs(overridden);
        restored.Songs.Should().ContainSingle().Which.Title.Should().Be("River Song");
        TopMatch(restored, CACHED_FRAGMENT)!.SpotifyTrackId.Should().Be(TRACK_ID);
        TopMatch(restored, OVERRIDE_FRAGMENT).Should().BeNull();
    }

    [Fact]
    public async Task SynchronousSaveChanges_MarksTheTrackDirtyToo()
    {
        LyricsIndexSnapshot empty = await GetSnapshotAsync();

        using (CantusDbContext context = CreateContext())
        {
            context.CachedLyrics.Add(new CachedLyricsEntity
            {
                TrackId = TRACK_ID,
                TrackName = "River Song",
                ArtistName = "The Band",
                PlainLyrics = CACHED_LYRICS,
            });
            context.SaveChanges();
        }

        LyricsIndexSnapshot refreshed = await GetSnapshotAsync();

        refreshed.Should().NotBeSameAs(empty);
        refreshed.Count.Should().Be(1);
    }

    [Fact]
    public async Task FailedSave_MarksNothing()
    {
        await WithRepositoryAsync(repository => repository.SaveLyricsAsync(Lyrics(CACHED_LYRICS)));
        LyricsIndexSnapshot built = await GetSnapshotAsync();

        using (CantusDbContext context = CreateContext())
        {
            context.CachedLyrics.Add(new CachedLyricsEntity
            {
                TrackId = TRACK_ID,
                TrackName = "Duplicate",
                ArtistName = "Duplicate",
                PlainLyrics = OVERRIDE_LYRICS,
            });
            Func<Task> save = () => context.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
        }

        (await GetSnapshotAsync()).Should().BeSameAs(built);
    }

    [Fact]
    public async Task UnrelatedWrite_MarksNothing()
    {
        await WithRepositoryAsync(repository => repository.SaveLyricsAsync(Lyrics(CACHED_LYRICS)));
        LyricsIndexSnapshot built = await GetSnapshotAsync();

        await WithRepositoryAsync(repository => repository.SetTrackOffsetAsync(TRACK_ID, 250));

        (await GetSnapshotAsync()).Should().BeSameAs(built);
    }
}
