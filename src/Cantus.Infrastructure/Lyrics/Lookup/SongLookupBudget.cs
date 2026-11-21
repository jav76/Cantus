using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// The per-user online lookup budget (singleton): at most
/// <see cref="SongLookupOptions.OnlineLookupsPerMinute"/> lookups per rolling
/// minute may call a lyrics provider. Keyed by Spotify user id, so the Session
/// id never reaches it. Users with no lookup inside the window are dropped.
/// </summary>
public sealed class SongLookupBudget
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _timeProvider;
    private readonly IOptions<SongLookupOptions> _options;
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _lookups = new(StringComparer.Ordinal);
    private DateTimeOffset _lastSweepUtc = DateTimeOffset.MinValue;

    public SongLookupBudget(TimeProvider timeProvider, IOptions<SongLookupOptions> options)
    {
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>Users currently holding a budget entry.</summary>
    public int TrackedUserCount
    {
        get
        {
            lock (_lock)
            {
                SweepIdle(_timeProvider.GetUtcNow());
                return _lookups.Count;
            }
        }
    }

    /// <summary>
    /// Spends one online lookup for <paramref name="spotifyUserId"/>. When refused,
    /// <paramref name="retryAfter"/> says how long until the next one is allowed.
    /// </summary>
    public bool TryAcquire(string spotifyUserId, out TimeSpan retryAfter)
    {
        ArgumentException.ThrowIfNullOrEmpty(spotifyUserId);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            SweepIdle(now);
            if (!_lookups.TryGetValue(spotifyUserId, out Queue<DateTimeOffset>? lookups))
            {
                lookups = new Queue<DateTimeOffset>();
                _lookups[spotifyUserId] = lookups;
            }

            retryAfter = GetBlockedFor(lookups, now);
            if (retryAfter > TimeSpan.Zero)
            {
                return false;
            }

            lookups.Enqueue(now);
            return true;
        }
    }

    /// <summary>
    /// How long until <paramref name="spotifyUserId"/> may spend an online lookup, or zero. Spends nothing.
    /// </summary>
    public TimeSpan GetRetryAfter(string spotifyUserId)
    {
        ArgumentException.ThrowIfNullOrEmpty(spotifyUserId);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            return _lookups.TryGetValue(spotifyUserId, out Queue<DateTimeOffset>? lookups)
                ? GetBlockedFor(lookups, now)
                : TimeSpan.Zero;
        }
    }

    private TimeSpan GetBlockedFor(Queue<DateTimeOffset> lookups, DateTimeOffset now)
    {
        Prune(lookups, now);
        int limit = Math.Max(1, _options.Value.OnlineLookupsPerMinute);
        return lookups.Count >= limit
            ? lookups.Peek() + Window - now
            : TimeSpan.Zero;
    }

    private void SweepIdle(DateTimeOffset now)
    {
        if (now - _lastSweepUtc < Window)
        {
            return;
        }

        _lastSweepUtc = now;
        foreach (KeyValuePair<string, Queue<DateTimeOffset>> entry in _lookups.ToList())
        {
            Prune(entry.Value, now);
            if (entry.Value.Count == 0)
            {
                _lookups.Remove(entry.Key);
            }
        }
    }

    private static void Prune(Queue<DateTimeOffset> lookups, DateTimeOffset now)
    {
        while (lookups.Count > 0 && lookups.Peek() + Window <= now)
        {
            lookups.Dequeue();
        }
    }
}
