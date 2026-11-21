namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// Remembers what each lyrics provider answered for a normalized fragment
/// (singleton, in memory only). Keyed by provider and fragment, so a provider
/// skipped by one lookup is never served from another provider's entry. Hits
/// live <see cref="HIT_TTL_MINUTES"/> minutes and definitive empty answers
/// <see cref="EMPTY_TTL_MINUTES"/>; the caller never stores unavailable or rate
/// limited outcomes. A cache hit spends no online budget.
/// </summary>
public sealed class SongLookupQueryCache
{
    public const int MAX_ENTRIES = 64;
    public const int HIT_TTL_MINUTES = 10;
    public const int EMPTY_TTL_MINUTES = 2;

    private static readonly TimeSpan HitTimeToLive = TimeSpan.FromMinutes(HIT_TTL_MINUTES);
    private static readonly TimeSpan EmptyTimeToLive = TimeSpan.FromMinutes(EMPTY_TTL_MINUTES);

    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private readonly Dictionary<CacheKey, CacheEntry> _entries = new();

    public SongLookupQueryCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGet(
        string providerName,
        string normalizedFragment,
        out IReadOnlyList<LyricsFragmentHit> hits)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        CacheKey key = new(providerName, normalizedFragment);

        lock (_lock)
        {
            if (_entries.TryGetValue(key, out CacheEntry? entry))
            {
                if (entry.ExpiresAtUtc > now)
                {
                    hits = entry.Hits;
                    return true;
                }

                _entries.Remove(key);
            }
        }

        hits = [];
        return false;
    }

    /// <summary>Stores a definitive answer; an empty list is cached for the shorter time.</summary>
    public void Store(string providerName, string normalizedFragment, IReadOnlyList<LyricsFragmentHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan timeToLive = hits.Count > 0 ? HitTimeToLive : EmptyTimeToLive;
        CacheKey key = new(providerName, normalizedFragment);

        lock (_lock)
        {
            if (!_entries.ContainsKey(key) && _entries.Count >= MAX_ENTRIES)
            {
                MakeRoom(now);
            }

            _entries[key] = new CacheEntry(hits.ToArray(), now, now.Add(timeToLive));
        }
    }

    private void MakeRoom(DateTimeOffset now)
    {
        foreach (CacheKey expired in _entries.Where(e => e.Value.ExpiresAtUtc <= now).Select(e => e.Key).ToList())
        {
            _entries.Remove(expired);
        }

        if (_entries.Count >= MAX_ENTRIES)
        {
            CacheKey oldest = _entries.MinBy(e => e.Value.StoredAtUtc).Key;
            _entries.Remove(oldest);
        }
    }

    private readonly record struct CacheKey(string ProviderName, string NormalizedFragment);

    private sealed record CacheEntry(
        IReadOnlyList<LyricsFragmentHit> Hits,
        DateTimeOffset StoredAtUtc,
        DateTimeOffset ExpiresAtUtc);
}
