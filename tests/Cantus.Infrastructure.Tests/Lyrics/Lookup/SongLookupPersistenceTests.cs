using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Lyrics.Lookup;
using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics.Lookup;

/// <summary>
/// Runs lookups against real SQLite with the change interceptor attached, to pin
/// that a lookup writes neither the lyrics cache nor anything the index would see.
/// </summary>
public sealed class SongLookupPersistenceTests : IDisposable
{
    private const string USER_ID = "spotify-user";
    private const string CACHED_LYRICS = "the river runs down to the sea tonight\nunder a silver moon we sing";
    private const string LOCAL_FRAGMENT = "the river runs down to the sea tonight";
    private const string ONLINE_FRAGMENT = "golden bells ring out across the frozen hills";
    private const string ONLINE_LYRICS = "golden bells ring out across the frozen hills\nand nobody hears them";
    private const int CACHED_TRACK_COUNT = 2;

    private readonly SqliteConnection _connection;
    private readonly LyricsSearchIndex _index;
    private readonly LyricsCorpusChangeInterceptor _interceptor;
    private readonly SongLookupOptions _options = new();

    public SongLookupPersistenceTests()
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

    private static ILyricsFetchProvider CreateProvider(string name, IReadOnlyList<LyricsFragmentHit> hits)
    {
        ILyricsFetchProvider provider = Substitute.For<ILyricsFetchProvider, ILyricsFragmentSearchProvider>();
        provider.ProviderName.Returns(name);
        ILyricsFragmentSearchProvider search = (ILyricsFragmentSearchProvider)provider;
        search.ProviderName.Returns(name);
        search.SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>()).Returns(hits);
        return provider;
    }

    private async Task<SongLookupResult> LookupAsync(string fragment, SongLookupScope scope)
    {
        using CantusDbContext context = CreateContext();
        LyricsCandidate candidate = new(
            NeteaseLyricsProvider.PROVIDER_NAME,
            "1",
            "Golden Bells",
            "Online Artist",
            null,
            TimeSpan.Zero,
            false,
            false,
            []);
        ILyricsFetchProvider[] providers =
        [
            CreateProvider(LrclibLyricsProvider.PROVIDER_NAME, []),
            CreateProvider(NeteaseLyricsProvider.PROVIDER_NAME, [new LyricsFragmentHit(candidate, ONLINE_LYRICS)]),
        ];

        SongLookupService service = new(
            new SqliteLyricsCorpusReader(context, TimeProvider.System),
            _index,
            providers,
            new ProviderRateGate(TimeProvider.System, Options.Create(_options), NullLogger<ProviderRateGate>.Instance),
            new SongLookupQueryCache(TimeProvider.System),
            new SongLookupBudget(TimeProvider.System, Options.Create(_options)),
            Options.Create(_options),
            TimeProvider.System,
            NullLogger<SongLookupService>.Instance);

        return await service.LookupAsync(fragment, scope, USER_ID);
    }

    private async Task<LyricsIndexSnapshot> GetSnapshotAsync()
    {
        using CantusDbContext context = CreateContext();
        return await _index.GetSnapshotAsync(new SqliteLyricsCorpusReader(context, TimeProvider.System));
    }

    private async Task<int> CountCachedRowsAsync()
    {
        using CantusDbContext context = CreateContext();
        return await context.CachedLyrics.CountAsync();
    }

    [Fact]
    public async Task Lookup_LocalAndOnline_LeavesTheCacheAndTheIndexUnchanged()
    {
        using (CantusDbContext context = CreateContext())
        {
            SqliteLyricsCacheRepository repository = CreateRepository(context);
            await repository.SaveLyricsAsync(new SyncedLyrics
            {
                TrackId = "t-river",
                Title = "River Song",
                Artist = "The Band",
                PlainLyrics = CACHED_LYRICS,
            });
            await repository.SaveLyricsAsync(new SyncedLyrics
            {
                TrackId = "t-meadow",
                Title = "Meadow",
                Artist = "The Band",
                PlainLyrics = "green grass grows in the meadow\nwhere the cattle wander slowly home",
            });
        }

        LyricsIndexSnapshot before = await GetSnapshotAsync();

        SongLookupResult local = await LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Auto);
        SongLookupResult online = await LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Online);

        local.Outcome.Should().Be(SongMatchOutcome.Accepted);
        local.Guesses[0].SpotifyTrackId.Should().Be("t-river");
        online.Guesses[0].Source.Should().Be(NeteaseLyricsProvider.PROVIDER_NAME);
        (await CountCachedRowsAsync()).Should().Be(CACHED_TRACK_COUNT);
        (await GetSnapshotAsync()).Should().BeSameAs(before);
    }
}
