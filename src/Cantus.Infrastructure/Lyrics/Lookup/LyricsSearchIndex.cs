using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// The in-memory search corpus of the song lookup (singleton).
/// <list type="bullet">
/// <item>Built lazily on the first lookup. The caller passes its scoped
/// <see cref="ILyricsCorpusReader"/>, so the singleton never captures a DbContext.</item>
/// <item>Afterwards only the track ids reported through <see cref="MarkDirty"/> are re-read
/// and swapped into a new snapshot (copy-on-write). The dirty set is taken before the read,
/// so a write that lands during a refresh is picked up by the next one.</item>
/// <item>Backstops: a full rebuild after <see cref="INDEX_MAX_AGE_HOURS"/> (writes that bypass
/// EF), after more than <see cref="MAX_DIRTY_BEFORE_REBUILD"/> dirty ids, and the snapshot is
/// dropped after <see cref="INDEX_IDLE_EVICT_MINUTES"/> without lookups.</item>
/// </list>
/// </summary>
public sealed class LyricsSearchIndex : IDisposable
{
    public const string LOCAL_SOURCE_NAME = "Local";
    public const int INDEX_MAX_AGE_HOURS = 6;
    public const int INDEX_IDLE_EVICT_MINUTES = 20;

    /// <summary>
    /// A scan stops after this long and reports itself truncated, which rules out an accepted outcome.
    /// </summary>
    public const int LOCAL_SCAN_BUDGET_MS = 1500;

    /// <summary>Above this many songs a warning is logged once: a full scan per lookup gets slow.</summary>
    public const int CORPUS_SCAN_WARN_ROWS = 20000;

    /// <summary>More dirty ids than this are cheaper to handle with one full rebuild.</summary>
    public const int MAX_DIRTY_BEFORE_REBUILD = 1000;

    /// <summary>Songs scored between two cancellation and time-budget checks.</summary>
    internal const int SCAN_CHECK_INTERVAL = 64;

    internal static readonly TimeSpan ScanBudget = TimeSpan.FromMilliseconds(LOCAL_SCAN_BUDGET_MS);

    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(INDEX_MAX_AGE_HOURS);
    private static readonly TimeSpan IdleEvictAfter = TimeSpan.FromMinutes(INDEX_IDLE_EVICT_MINUTES);

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LyricsSearchIndex> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _dirtyLock = new();
    private readonly ITimer _idleTimer;
    private HashSet<string> _dirty = new(StringComparer.Ordinal);
    private bool _rebuildRequested;
    private volatile LyricsIndexSnapshot? _snapshot;

    // Writer state, only touched while holding _gate.
    private LyricTermTableBuilder _builder = new();
    private Dictionary<string, IndexedSong> _songs = new(StringComparer.Ordinal);
    private bool _largeCorpusWarned;

    public LyricsSearchIndex(TimeProvider timeProvider, ILogger<LyricsSearchIndex> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _idleTimer = timeProvider.CreateTimer(
            _ => EvictIfIdle(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>True while a snapshot is held in memory.</summary>
    public bool IsLoaded => _snapshot is not null;

    /// <summary>Records tracks whose cache or override rows changed; the next lookup re-reads them.</summary>
    public void MarkDirty(IEnumerable<string> trackIds)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        lock (_dirtyLock)
        {
            if (_rebuildRequested)
            {
                return;
            }

            _dirty.UnionWith(trackIds);
            if (_dirty.Count > MAX_DIRTY_BEFORE_REBUILD)
            {
                _dirty.Clear();
                _rebuildRequested = true;
            }
        }
    }

    /// <summary>
    /// The current snapshot, built or refreshed first when needed. Concurrent callers
    /// wait for one build instead of each reading the database.
    /// </summary>
    public async Task<LyricsIndexSnapshot> GetSnapshotAsync(
        ILyricsCorpusReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _idleTimer.Change(IdleEvictAfter, Timeout.InfiniteTimeSpan);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            LyricsIndexSnapshot? current = _snapshot;
            (HashSet<string> dirty, bool rebuildRequested) = TakeDirty();
            try
            {
                if (current is null || rebuildRequested || _timeProvider.GetUtcNow() - current.BuiltAtUtc >= MaxAge)
                {
                    return await RebuildAsync(reader, cancellationToken);
                }

                return dirty.Count == 0
                    ? current
                    : await RefreshAsync(current, dirty, reader, cancellationToken);
            }
            catch
            {
                RestoreDirty(dirty, rebuildRequested);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Scores the fragment against every song of the snapshot. Stops after
    /// <see cref="LOCAL_SCAN_BUDGET_MS"/> and marks the result truncated.
    /// </summary>
    public LyricsScanResult Scan(
        LyricsIndexSnapshot snapshot,
        LyricFragment fragment,
        CancellationToken cancellationToken = default) =>
        Scan(snapshot, fragment, ScanBudget, cancellationToken);

    internal LyricsScanResult Scan(
        LyricsIndexSnapshot snapshot,
        LyricFragment fragment,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        long started = _timeProvider.GetTimestamp();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        LyricFragmentScorer scorer = new(fragment, snapshot.Terms);
        IReadOnlyList<IndexedSong> songs = snapshot.Songs;
        List<SongCandidate> candidates = new();
        bool truncated = false;
        int scanned = 0;

        for (int i = 0; i < songs.Count; i++)
        {
            if (i > 0 && i % SCAN_CHECK_INTERVAL == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_timeProvider.GetElapsedTime(started) >= budget)
                {
                    truncated = true;
                    break;
                }
            }

            IndexedSong song = songs[i];
            scanned++;
            if (song.ExpiresAtUtc is { } expires && expires <= now)
            {
                continue;
            }

            LyricMatch match = scorer.Score(song.Document);
            if (match.Score < LyricMatchConstants.RESULT_MIN_SCORE)
            {
                continue;
            }

            candidates.Add(new SongCandidate(
                SongCandidateOrigin.Local,
                LOCAL_SOURCE_NAME,
                song.TrackId,
                song.Title,
                song.Artist,
                song.Album,
                null,
                match.Score,
                true,
                song.Document.GetMatchedLines(match),
                snapshot.Terms.GetTerms(song.Document)));
        }

        TimeSpan elapsed = _timeProvider.GetElapsedTime(started);
        if (truncated)
        {
            _logger.LogWarning(
                "Lyrics scan stopped after {ElapsedMs} ms with {SongsScanned} of {CorpusSize} songs scored",
                (long)elapsed.TotalMilliseconds,
                scanned,
                songs.Count);
        }

        return new LyricsScanResult(candidates, songs.Count, scanned, truncated, elapsed);
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
    }

    private async Task<LyricsIndexSnapshot> RebuildAsync(
        ILyricsCorpusReader reader,
        CancellationToken cancellationToken)
    {
        long started = _timeProvider.GetTimestamp();
        IReadOnlyList<LyricsCorpusEntry> entries = await reader.ReadAllAsync(cancellationToken);

        LyricTermTableBuilder builder = new();
        Dictionary<string, IndexedSong> songs = new(entries.Count, StringComparer.Ordinal);
        foreach (LyricsCorpusEntry entry in entries)
        {
            AddSong(builder, songs, entry);
        }

        _builder = builder;
        _songs = songs;
        LyricsIndexSnapshot snapshot = Publish(_timeProvider.GetUtcNow());

        _logger.LogDebug(
            "Built the lyrics search index: {SongCount} songs, {TermCount} terms in {ElapsedMs} ms",
            snapshot.Count,
            snapshot.Terms.TermCount,
            (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds);
        WarnIfLarge(snapshot.Count);
        return snapshot;
    }

    private async Task<LyricsIndexSnapshot> RefreshAsync(
        LyricsIndexSnapshot current,
        HashSet<string> dirty,
        ILyricsCorpusReader reader,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LyricsCorpusEntry> entries = await reader.ReadAsync(dirty, cancellationToken);

        foreach (string trackId in dirty)
        {
            if (_songs.Remove(trackId, out IndexedSong? previous))
            {
                _builder.RemoveDocument(previous.Document);
            }
        }

        foreach (LyricsCorpusEntry entry in entries)
        {
            if (dirty.Contains(entry.TrackId))
            {
                AddSong(_builder, _songs, entry);
            }
        }

        LyricsIndexSnapshot snapshot = Publish(current.BuiltAtUtc);
        _logger.LogDebug(
            "Refreshed {DirtyCount} tracks in the lyrics search index ({SongCount} songs)",
            dirty.Count,
            snapshot.Count);
        WarnIfLarge(snapshot.Count);
        return snapshot;
    }

    private static void AddSong(
        LyricTermTableBuilder builder,
        Dictionary<string, IndexedSong> songs,
        LyricsCorpusEntry entry)
    {
        NormalizedLyricText text = LyricTextNormalizer.Normalize(entry.PlainLyrics);
        if (text.Tokens.Length == 0)
        {
            return;
        }

        if (songs.Remove(entry.TrackId, out IndexedSong? duplicate))
        {
            builder.RemoveDocument(duplicate.Document);
        }

        LyricDocument document = builder.AddDocument(text);
        songs.Add(
            entry.TrackId,
            new IndexedSong(entry.TrackId, entry.Title, entry.Artist, entry.Album, document, entry.ExpiresAtUtc));
    }

    private LyricsIndexSnapshot Publish(DateTimeOffset builtAtUtc)
    {
        LyricsIndexSnapshot snapshot = new(_songs.Values.ToArray(), _builder.Build(), builtAtUtc);
        _snapshot = snapshot;
        return snapshot;
    }

    private (HashSet<string> Dirty, bool RebuildRequested) TakeDirty()
    {
        lock (_dirtyLock)
        {
            HashSet<string> dirty = _dirty;
            bool rebuildRequested = _rebuildRequested;
            _dirty = new HashSet<string>(StringComparer.Ordinal);
            _rebuildRequested = false;
            return (dirty, rebuildRequested);
        }
    }

    private void RestoreDirty(HashSet<string> dirty, bool rebuildRequested)
    {
        lock (_dirtyLock)
        {
            _dirty.UnionWith(dirty);
            if (rebuildRequested || _dirty.Count > MAX_DIRTY_BEFORE_REBUILD)
            {
                _rebuildRequested = true;
            }

            if (_rebuildRequested)
            {
                _dirty.Clear();
            }
        }
    }

    private void WarnIfLarge(int songCount)
    {
        if (songCount <= CORPUS_SCAN_WARN_ROWS || _largeCorpusWarned)
        {
            return;
        }

        _largeCorpusWarned = true;
        _logger.LogWarning(
            "The lyrics search index holds {SongCount} songs, above {WarnRows}; every lookup scans them all",
            songCount,
            CORPUS_SCAN_WARN_ROWS);
    }

    private void EvictIfIdle()
    {
        if (!_gate.Wait(0))
        {
            // A lookup is building or refreshing, so the index is not idle.
            _idleTimer.Change(IdleEvictAfter, Timeout.InfiniteTimeSpan);
            return;
        }

        try
        {
            if (_snapshot is null)
            {
                return;
            }

            _snapshot = null;
            _builder = new LyricTermTableBuilder();
            _songs = new Dictionary<string, IndexedSong>(StringComparer.Ordinal);
            lock (_dirtyLock)
            {
                _dirty.Clear();
                _rebuildRequested = false;
            }

            _logger.LogDebug("Dropped the idle lyrics search index");
        }
        finally
        {
            _gate.Release();
        }
    }
}
