using System.Diagnostics;
using System.Globalization;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Lyrics.Lookup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Cantus.Infrastructure.Tests.Lyrics.Lookup;

public sealed class LyricsSearchIndexTests : IDisposable
{
    private const string RIVER_LYRICS = "the river runs down to the sea tonight\nunder a silver moon we sing along";
    private const string RIVER_FRAGMENT = "the river runs down to the sea";
    private const string MEADOW_LYRICS = "green grass grows in the meadow\nwhere the cattle wander slowly home";

    private const int TIMING_SONG_COUNT = 5000;
    private const int TIMING_SEED = 4242;
    private const int TIMING_VOCABULARY_SIZE = 3000;
    private const int TIMING_LINES_PER_SONG = 40;
    private const int TIMING_MIN_LINE_WORDS = 5;
    private const int TIMING_MAX_LINE_WORDS = 9;
    private const int SYLLABLES_PER_WORD_MAX = 3;
    private const int SMALL_CORPUS_SONGS = 100;
    private const int SCAN_CHECK_INTERVAL = 64;
    private const int LOOKUP_INTERVAL_MINUTES = 15;

    private static readonly string[] Syllables =
    [
        "ba", "ko", "mi", "ra", "te", "lu", "sen", "dor", "vi", "na", "pal", "rin", "go", "fe", "shi", "tam",
    ];

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, LyricsCorpusEntry> _rows = new(StringComparer.Ordinal);
    private readonly List<string[]> _partialReads = new();
    private readonly ILyricsCorpusReader _reader = Substitute.For<ILyricsCorpusReader>();
    private readonly LyricsSearchIndex _index;
    private readonly ITestOutputHelper _output;

    public LyricsSearchIndexTests(ITestOutputHelper output)
    {
        _output = output;
        _index = new LyricsSearchIndex(_timeProvider, NullLogger<LyricsSearchIndex>.Instance);
        _reader.ReadAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<LyricsCorpusEntry>>(_rows.Values.ToList()));
        _reader.ReadAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string[] ids = call.Arg<IReadOnlyCollection<string>>().Order(StringComparer.Ordinal).ToArray();
                _partialReads.Add(ids);
                IReadOnlyList<LyricsCorpusEntry> found = ids
                    .Where(_rows.ContainsKey)
                    .Select(id => _rows[id])
                    .ToList();
                return Task.FromResult(found);
            });
    }

    public void Dispose()
    {
        _index.Dispose();
    }

    private void Store(string trackId, string lyrics, string? title = null) =>
        _rows[trackId] = new LyricsCorpusEntry(trackId, title ?? $"Title {trackId}", "Artist", null, lyrics, null);

    private static LyricFragment Fragment(string text) => LyricTextNormalizer.PrepareFragment(text);

    [Fact]
    public async Task GetSnapshotAsync_BuildsOnceAndReusesTheSnapshot()
    {
        Store("river", RIVER_LYRICS);
        Store("meadow", MEADOW_LYRICS);

        LyricsIndexSnapshot first = await _index.GetSnapshotAsync(_reader);
        LyricsIndexSnapshot second = await _index.GetSnapshotAsync(_reader);

        first.Songs.Select(s => s.TrackId).Should().BeEquivalentTo("river", "meadow");
        first.Terms.DocumentCount.Should().Be(2);
        second.Should().BeSameAs(first);
        await _reader.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
        _partialReads.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSnapshotAsync_ConcurrentFirstCallsShareOneBuild()
    {
        Store("river", RIVER_LYRICS);

        LyricsIndexSnapshot[] snapshots = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(() => _index.GetSnapshotAsync(_reader))));

        snapshots.Distinct().Should().ContainSingle();
        await _reader.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkDirty_RereadsOnlyTheDirtyTracksIntoANewSnapshot()
    {
        Store("river", RIVER_LYRICS);
        Store("meadow", MEADOW_LYRICS);
        LyricsIndexSnapshot before = await _index.GetSnapshotAsync(_reader);

        Store("meadow", "a brand new chorus about the stars above");
        _index.MarkDirty(["meadow"]);
        LyricsIndexSnapshot after = await _index.GetSnapshotAsync(_reader);

        _partialReads.Should().ContainSingle().Which.Should().Equal("meadow");
        await _reader.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
        after.Should().NotBeSameAs(before);
        after.Count.Should().Be(2);
        after.Terms.GetTerms(after.Songs.Single(s => s.TrackId == "meadow").Document)
            .Should().Contain("stars");
        before.Terms.GetTerms(before.Songs.Single(s => s.TrackId == "meadow").Document)
            .Should().NotContain("stars", "a published snapshot never changes");
    }

    [Fact]
    public async Task MarkDirty_ForATrackThatLostItsLyrics_RemovesItAndItsDocumentFrequency()
    {
        Store("river", RIVER_LYRICS);
        Store("meadow", MEADOW_LYRICS);
        await _index.GetSnapshotAsync(_reader);

        _rows.Remove("meadow");
        _index.MarkDirty(["meadow"]);
        LyricsIndexSnapshot after = await _index.GetSnapshotAsync(_reader);

        after.Songs.Select(s => s.TrackId).Should().Equal("river");
        after.Terms.DocumentCount.Should().Be(1);
        after.Terms.TryGetTermId("meadow", out int meadowId).Should().BeTrue();
        after.Terms.GetDocumentFrequency(meadowId).Should().Be(0);
    }

    [Fact]
    public async Task MarkDirty_DuringARefresh_IsPickedUpByTheNextRefresh()
    {
        Store("river", RIVER_LYRICS);
        await _index.GetSnapshotAsync(_reader);
        bool firstRead = true;
        _reader.ReadAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string[] ids = call.Arg<IReadOnlyCollection<string>>().ToArray();
                _partialReads.Add(ids);
                if (firstRead)
                {
                    firstRead = false;
                    Store("meadow", MEADOW_LYRICS);
                    _index.MarkDirty(["meadow"]);
                }

                IReadOnlyList<LyricsCorpusEntry> found = ids.Where(_rows.ContainsKey).Select(id => _rows[id]).ToList();
                return Task.FromResult(found);
            });

        _index.MarkDirty(["river"]);
        LyricsIndexSnapshot duringWrite = await _index.GetSnapshotAsync(_reader);
        LyricsIndexSnapshot afterWrite = await _index.GetSnapshotAsync(_reader);

        duringWrite.Songs.Select(s => s.TrackId).Should().Equal("river");
        afterWrite.Songs.Select(s => s.TrackId).Should().BeEquivalentTo("river", "meadow");
        _partialReads.Select(ids => string.Join(',', ids)).Should().Equal("river", "meadow");
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenTheRefreshFails_KeepsTheDirtyIdsForTheNextCall()
    {
        Store("river", RIVER_LYRICS);
        LyricsIndexSnapshot before = await _index.GetSnapshotAsync(_reader);
        _reader.ReadAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<IReadOnlyList<LyricsCorpusEntry>>(new InvalidOperationException("db down")),
                _ => Task.FromResult<IReadOnlyList<LyricsCorpusEntry>>([_rows["meadow"]]));

        Store("meadow", MEADOW_LYRICS);
        _index.MarkDirty(["meadow"]);
        Func<Task> failing = () => _index.GetSnapshotAsync(_reader);
        await failing.Should().ThrowAsync<InvalidOperationException>();
        LyricsIndexSnapshot after = await _index.GetSnapshotAsync(_reader);

        after.Should().NotBeSameAs(before);
        after.Songs.Select(s => s.TrackId).Should().BeEquivalentTo("river", "meadow");
    }

    [Fact]
    public async Task GetSnapshotAsync_AfterTheMaximumAge_RebuildsFromScratch()
    {
        Store("river", RIVER_LYRICS);
        LyricsIndexSnapshot first = await _index.GetSnapshotAsync(_reader);

        // Steady lookups keep the idle timer from dropping the snapshot first.
        TimeSpan step = TimeSpan.FromMinutes(LOOKUP_INTERVAL_MINUTES);
        TimeSpan maxAge = TimeSpan.FromHours(LyricsSearchIndex.INDEX_MAX_AGE_HOURS);
        for (TimeSpan elapsed = step; elapsed < maxAge; elapsed += step)
        {
            _timeProvider.Advance(step);
            (await _index.GetSnapshotAsync(_reader)).Should().BeSameAs(first);
        }

        _timeProvider.Advance(step);
        LyricsIndexSnapshot rebuilt = await _index.GetSnapshotAsync(_reader);

        rebuilt.Should().NotBeSameAs(first);
        rebuilt.BuiltAtUtc.Should().Be(_timeProvider.GetUtcNow());
        await _reader.Received(2).ReadAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkDirty_WithTooManyIds_FallsBackToAFullRebuild()
    {
        Store("river", RIVER_LYRICS);
        await _index.GetSnapshotAsync(_reader);

        _index.MarkDirty(Enumerable.Range(0, LyricsSearchIndex.MAX_DIRTY_BEFORE_REBUILD + 1)
            .Select(i => i.ToString(CultureInfo.InvariantCulture)));
        await _index.GetSnapshotAsync(_reader);

        await _reader.Received(2).ReadAllAsync(Arg.Any<CancellationToken>());
        _partialReads.Should().BeEmpty();
    }

    [Fact]
    public async Task IdleTimer_DropsTheSnapshotAfterTwentyMinutesWithoutLookups()
    {
        Store("river", RIVER_LYRICS);
        await _index.GetSnapshotAsync(_reader);

        _timeProvider.Advance(TimeSpan.FromMinutes(LyricsSearchIndex.INDEX_IDLE_EVICT_MINUTES - 1));
        await _index.GetSnapshotAsync(_reader);
        _timeProvider.Advance(TimeSpan.FromMinutes(LyricsSearchIndex.INDEX_IDLE_EVICT_MINUTES - 1));
        _index.IsLoaded.Should().BeTrue("each lookup restarts the idle timer");

        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        _index.IsLoaded.Should().BeFalse();

        await _index.GetSnapshotAsync(_reader);
        _index.IsLoaded.Should().BeTrue();
        await _reader.Received(2).ReadAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scan_ReturnsTheMatchingSongWithItsMatchedLines()
    {
        Store("river", RIVER_LYRICS, "River Song");
        Store("meadow", MEADOW_LYRICS);
        LyricsIndexSnapshot snapshot = await _index.GetSnapshotAsync(_reader);

        LyricsScanResult result = _index.Scan(snapshot, Fragment(RIVER_FRAGMENT));

        result.Truncated.Should().BeFalse();
        result.CorpusSize.Should().Be(2);
        result.SongsScanned.Should().Be(2);
        SongCandidate top = result.Candidates.MaxBy(c => c.Score)!;
        top.SpotifyTrackId.Should().Be("river");
        top.Origin.Should().Be(SongCandidateOrigin.Local);
        top.SourceName.Should().Be(LyricsSearchIndex.LOCAL_SOURCE_NAME);
        top.Title.Should().Be("River Song");
        top.Score.Should().BeGreaterThan(LyricMatchConstants.ACCEPT_SCORE);
        top.MatchedLines.Should().Equal("the river runs down to the sea tonight");
        top.LyricTokens.Should().Contain("silver");
        result.Candidates.Should().OnlyContain(c => c.Score >= LyricMatchConstants.RESULT_MIN_SCORE);
    }

    [Fact]
    public async Task Scan_SkipsSongsWhoseCacheRowHasExpired()
    {
        _rows["river"] = new LyricsCorpusEntry(
            "river",
            "River",
            "Artist",
            null,
            RIVER_LYRICS,
            _timeProvider.GetUtcNow().AddMinutes(5));
        LyricsIndexSnapshot snapshot = await _index.GetSnapshotAsync(_reader);

        _index.Scan(snapshot, Fragment(RIVER_FRAGMENT)).Candidates.Should().ContainSingle();
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        _index.Scan(snapshot, Fragment(RIVER_FRAGMENT)).Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_WhenTheTimeBudgetRunsOut_StopsAndReportsTruncated()
    {
        for (int i = 0; i < SMALL_CORPUS_SONGS; i++)
        {
            Store(i.ToString(CultureInfo.InvariantCulture), RIVER_LYRICS);
        }

        LyricsIndexSnapshot snapshot = await _index.GetSnapshotAsync(_reader);

        LyricsScanResult result = _index.Scan(
            snapshot,
            Fragment(RIVER_FRAGMENT),
            TimeSpan.Zero,
            CancellationToken.None);

        result.Truncated.Should().BeTrue();
        result.SongsScanned.Should().Be(SCAN_CHECK_INTERVAL);
        result.CorpusSize.Should().Be(SMALL_CORPUS_SONGS);
    }

    [Fact]
    public async Task Scan_WhenCancelled_Throws()
    {
        Store("river", RIVER_LYRICS);
        LyricsIndexSnapshot snapshot = await _index.GetSnapshotAsync(_reader);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Action scan = () => _index.Scan(snapshot, Fragment(RIVER_FRAGMENT), cancelled.Token);

        scan.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public async Task Timing_BuildRefreshAndScanOfFiveThousandSyntheticSongs()
    {
        Random random = new(TIMING_SEED);
        string[] vocabulary = SyntheticVocabulary(random);
        for (int i = 0; i < TIMING_SONG_COUNT; i++)
        {
            Store($"synthetic-{i.ToString(CultureInfo.InvariantCulture)}", SyntheticLyrics(random, vocabulary));
        }

        LyricsSearchIndex index = new(TimeProvider.System, NullLogger<LyricsSearchIndex>.Instance);
        long allocatedBefore = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch build = Stopwatch.StartNew();
        LyricsIndexSnapshot snapshot = await index.GetSnapshotAsync(_reader);
        build.Stop();
        long retained = GC.GetTotalMemory(forceFullCollection: true) - allocatedBefore;

        Store("synthetic-0", SyntheticLyrics(random, vocabulary));
        index.MarkDirty(["synthetic-0"]);
        Stopwatch refresh = Stopwatch.StartNew();
        snapshot = await index.GetSnapshotAsync(_reader);
        refresh.Stop();

        LyricFragment fragment = Fragment("i wait by the golden river tonight");
        index.Scan(snapshot, fragment);
        Stopwatch scan = Stopwatch.StartNew();
        LyricsScanResult result = index.Scan(snapshot, fragment);
        scan.Stop();

        _output.WriteLine(
            "{0} songs, {1} terms: build {2} ms, one-track refresh {3} ms, scan {4} ms (warm), "
                + "about {5:F1} MB retained by the index",
            snapshot.Count,
            snapshot.Terms.TermCount,
            build.ElapsedMilliseconds,
            refresh.ElapsedMilliseconds,
            scan.ElapsedMilliseconds,
            retained / (1024.0 * 1024.0));
        result.CorpusSize.Should().Be(TIMING_SONG_COUNT);
        index.Dispose();
    }

    private static string[] SyntheticVocabulary(Random random)
    {
        HashSet<string> words = new(StringComparer.Ordinal);
        while (words.Count < TIMING_VOCABULARY_SIZE)
        {
            words.Add(SyntheticWord(random));
        }

        return words.ToArray();
    }

    private static string SyntheticWord(Random random)
    {
        int syllables = random.Next(1, SYLLABLES_PER_WORD_MAX + 1);
        return string.Concat(Enumerable.Range(0, syllables).Select(_ => Syllables[random.Next(Syllables.Length)]));
    }

    private static string SyntheticLyrics(Random random, string[] vocabulary)
    {
        IEnumerable<string> lines = Enumerable.Range(0, TIMING_LINES_PER_SONG).Select(_ => string.Join(
            ' ',
            Enumerable
                .Range(0, random.Next(TIMING_MIN_LINE_WORDS, TIMING_MAX_LINE_WORDS + 1))
                .Select(_ => vocabulary[random.Next(vocabulary.Length)])));
        return string.Join('\n', lines);
    }
}
