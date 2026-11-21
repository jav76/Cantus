using Cantus.Core.Models;

namespace Cantus.Server.BackgroundServices;

/// <summary>
/// The adaptive polling cadence: how long to wait before the next Spotify poll, given an
/// owner's playback state, viewer visibility and the rate-limit cooldown. Pure decisions with
/// no I/O, timers or clock; <see cref="ActiveUsersPlaybackMonitor"/> runs the loop and keeps the
/// per-owner state (how long playback has been paused or idle). The cadences are described in
/// docs/architecture/adaptive-polling.md.
/// </summary>
public sealed class PollingCadencePolicy
{
    /// <summary>How often the loop checks for a viewer while nobody is connected.</summary>
    public const int NO_CONNECTED_CLIENTS_DELAY_MS = 1000;

    /// <summary>The pause after an unexpected error in the loop itself.</summary>
    public const int LOOP_ERROR_DELAY_MS = 3000;

    private const int MIN_LOOP_DELAY_MS = 500;
    private const int MIN_RATE_LIMITED_DELAY_MS = 1000;

    // The loop never sleeps longer than this, so new viewers and schedule changes are
    // picked up even when every owner is on a long backoff.
    private const int MAX_LOOP_DELAY_MS = 10000;

    private static readonly TimeSpan PausedExtendedAfter = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PausedDeepAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleExtendedAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IdleDeepAfter = TimeSpan.FromMinutes(10);

    private readonly PlaybackPollerOptions _options;

    public PollingCadencePolicy(PlaybackPollerOptions options)
    {
        _options = options;
    }

    /// <summary>The delay after an owner's poll failed.</summary>
    public int PollErrorIntervalMs => _options.PausedPollIntervalMs;

    /// <summary>The delay while viewers are connected but none is subscribed to an owner.</summary>
    public int NoActiveUsersDelayMs => _options.IdlePollIntervalMs;

    /// <summary>
    /// The interval until an owner's next poll.
    /// </summary>
    /// <param name="playback">The latest playback, or null when Spotify reports none (idle).</param>
    /// <param name="isVisible">Whether any of the owner's viewers is visible.</param>
    /// <param name="timeInState">How long the owner has been paused, or idle. Ignored while playing.</param>
    public int GetUserPollIntervalMs(PlaybackState? playback, bool isVisible, TimeSpan timeInState)
    {
        if (playback is null)
        {
            return GetIdleIntervalMs(isVisible, timeInState);
        }

        return playback.IsPlaying
            ? GetPlayingIntervalMs(playback, isVisible)
            : GetPausedIntervalMs(isVisible, timeInState);
    }

    /// <summary>The loop delay while Spotify polling is paused for a rate-limit cooldown.</summary>
    public int GetRateLimitedLoopDelayMs(TimeSpan remaining)
    {
        return (int)Math.Clamp(remaining.TotalMilliseconds, MIN_RATE_LIMITED_DELAY_MS, MAX_LOOP_DELAY_MS);
    }

    /// <summary>The poll interval reported in diagnostics during a rate-limit cooldown.</summary>
    public int GetRateLimitedReportedIntervalMs(TimeSpan remaining)
    {
        return (int)Math.Min(remaining.TotalMilliseconds, MAX_LOOP_DELAY_MS);
    }

    /// <summary>
    /// The loop delay until the earliest scheduled owner poll, or the active cadence when no
    /// owner has a poll scheduled.
    /// </summary>
    public int GetLoopDelayMs(DateTimeOffset? earliestNextPollUtc, DateTimeOffset nowUtc)
    {
        if (earliestNextPollUtc is null)
        {
            return _options.ActivePollIntervalMs;
        }

        TimeSpan wait = earliestNextPollUtc.Value - nowUtc;
        return (int)Math.Clamp(wait.TotalMilliseconds, MIN_LOOP_DELAY_MS, MAX_LOOP_DELAY_MS);
    }

    private int GetPlayingIntervalMs(PlaybackState playback, bool isVisible)
    {
        if (!isVisible)
        {
            return _options.BackgroundPollIntervalMs;
        }

        TimeSpan duration = playback.CurrentTrack?.Duration ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero)
        {
            return _options.ActivePollIntervalMs;
        }

        TimeSpan progress = playback.Progress;
        TimeSpan remaining = duration > progress ? duration - progress : TimeSpan.Zero;

        if (remaining.TotalMilliseconds <= _options.ImminentEndThresholdMs)
        {
            return _options.ImminentEndPollIntervalMs;
        }

        if (remaining.TotalMilliseconds <= _options.ApproachingEndThresholdMs)
        {
            return _options.ApproachingEndPollIntervalMs;
        }

        return _options.ActivePollIntervalMs;
    }

    private int GetPausedIntervalMs(bool isVisible, TimeSpan pausedFor)
    {
        if (!isVisible)
        {
            return Math.Max(_options.PausedDeepPollIntervalMs, _options.BackgroundPollIntervalMs);
        }

        if (pausedFor > PausedDeepAfter)
        {
            return _options.PausedDeepPollIntervalMs;
        }

        return pausedFor > PausedExtendedAfter
            ? _options.PausedExtendedPollIntervalMs
            : _options.PausedPollIntervalMs;
    }

    private int GetIdleIntervalMs(bool isVisible, TimeSpan idleFor)
    {
        if (!isVisible)
        {
            return Math.Max(_options.IdleDeepPollIntervalMs, _options.BackgroundPollIntervalMs);
        }

        if (idleFor > IdleDeepAfter)
        {
            return _options.IdleDeepPollIntervalMs;
        }

        return idleFor > IdleExtendedAfter
            ? _options.IdleExtendedPollIntervalMs
            : _options.IdlePollIntervalMs;
    }
}
