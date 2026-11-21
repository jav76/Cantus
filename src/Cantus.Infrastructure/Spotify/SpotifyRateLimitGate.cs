namespace Cantus.Infrastructure.Spotify;

/// <summary>
/// Shared Spotify 429 back-off. Every Spotify caller reports a rate limit here,
/// so the playback poller and on-demand commands pause together.
/// </summary>
public sealed class SpotifyRateLimitGate
{
    private const int DEFAULT_RETRY_AFTER_SECONDS = 60;

    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private DateTimeOffset _limitedUntilUtc = DateTimeOffset.MinValue;

    public SpotifyRateLimitGate(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateTimeOffset LimitedUntilUtc
    {
        get
        {
            lock (_lock)
            {
                return _limitedUntilUtc;
            }
        }
    }

    public bool IsLimited => Remaining > TimeSpan.Zero;

    public TimeSpan Remaining
    {
        get
        {
            TimeSpan remaining = LimitedUntilUtc - _timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Starts or extends the pause. A zero or negative Retry-After uses the
    /// default, and a shorter report never ends an active pause early.
    /// </summary>
    /// <returns>The effective Retry-After that was applied.</returns>
    public TimeSpan Report(TimeSpan retryAfter)
    {
        TimeSpan effective = retryAfter > TimeSpan.Zero
            ? retryAfter
            : TimeSpan.FromSeconds(DEFAULT_RETRY_AFTER_SECONDS);
        DateTimeOffset candidate = _timeProvider.GetUtcNow().Add(effective);

        lock (_lock)
        {
            if (candidate > _limitedUntilUtc)
            {
                _limitedUntilUtc = candidate;
            }
        }

        return effective;
    }
}
