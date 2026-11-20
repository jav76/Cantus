using System;
using Cantus.Client.Services;

namespace Cantus.Client.ViewModels;

/// <summary>
/// The optimistic seek anchor owned by <see cref="PlaybackTransport"/>.
/// Tap-to-seek is applied optimistically: the stage jumps to the tapped line
/// at once, and polls that still report the pre-seek position (Spotify's API
/// is eventually consistent) are ignored until one agrees with the seek or
/// the confirmation window lapses.
/// </summary>
internal sealed class OptimisticSeekAnchor
{
    // The window covers the hub's two follow-up polls plus one regular poll.
    private const long SEEK_CONFIRM_TOLERANCE_MS = 1000;
    private const long SEEK_CONFIRM_TIMEOUT_MS = 4000;

    private PlaybackStatePayload? _lastServerState;
    private string? _pendingSeekTrackId;
    private long _pendingSeekIssuedAtMs;
    private long? _seekDisplayFloorMs;
    private int _generation;

    /// <summary>
    /// Records a server poll and merges it with any optimistic seek.
    /// <paramref name="expected"/> is the effective state before this poll.
    /// </summary>
    public PlaybackStatePayload Reconcile(PlaybackStatePayload incoming, PlaybackStatePayload? expected, long localNow)
    {
        _lastServerState = incoming;
        if (_pendingSeekTrackId is null && _seekDisplayFloorMs is null)
        {
            return incoming;
        }

        return ReconcileWithOptimisticSeek(incoming, expected, localNow);
    }

    /// <summary>
    /// Anchors a line seek and returns the optimistic copy of
    /// <paramref name="state"/> positioned at <paramref name="targetMs"/>.
    /// </summary>
    public PlaybackStatePayload Begin(
        PlaybackStatePayload state,
        long targetMs,
        bool isPlaying,
        long issuedAtMs,
        long displayFloorMs)
    {
        _pendingSeekTrackId = state.CurrentTrack?.Id;
        _pendingSeekIssuedAtMs = issuedAtMs;
        _seekDisplayFloorMs = displayFloorMs;
        return CopyWithProgress(state, targetMs, isPlaying, DateTimeOffset.FromUnixTimeMilliseconds(issuedAtMs));
    }

    /// <summary>Starts a new seek generation so only the latest seek may revert.</summary>
    public int NextGeneration() => ++_generation;

    public bool IsCurrentGeneration(int generation) => generation == _generation;

    /// <summary>
    /// Drops the anchor and returns the last poll to restore, or null when
    /// there is no track to restore.
    /// </summary>
    public PlaybackStatePayload? Revert()
    {
        Clear();
        return _lastServerState?.CurrentTrack is null ? null : _lastServerState;
    }

    public void Clear()
    {
        _pendingSeekTrackId = null;
        _seekDisplayFloorMs = null;
    }

    public void Reset()
    {
        _lastServerState = null;
        Clear();
    }

    /// <summary>
    /// After a line seek the latency-compensated position sits just before
    /// the tapped line until the new audio has had time to arrive (and
    /// indefinitely while paused). Holds the display position on the line
    /// until the estimate catches up, so the previous line never flashes back.
    /// </summary>
    public long HoldAtFloor(long displayPositionMs)
    {
        if (_seekDisplayFloorMs is long floorMs)
        {
            if (displayPositionMs >= floorMs)
            {
                _seekDisplayFloorMs = null;
            }
            else
            {
                return floorMs;
            }
        }

        return displayPositionMs;
    }

    /// <summary>
    /// Raw track position at which a synced line is sung: its timestamp with
    /// the per-track offset undone (the display runs at position + offset).
    /// Latency compensation is deliberately NOT added. It models audio still in
    /// flight during continuous playback, but a seek flushes that pipeline, so
    /// the first audible sample is the seek target itself; adding the latency
    /// would clip the start of the line (200ms by default, up to a second).
    /// The cost - the steady-state formula placing the display just before the
    /// line for that long - is covered by the display floor instead.
    /// </summary>
    public static long ComputeLineSeekTargetMs(long lineTimestampMs, int userOffsetMs, long durationMs)
    {
        long targetMs = lineTimestampMs - userOffsetMs;
        return durationMs > 0 ? Math.Clamp(targetMs, 0, durationMs) : Math.Max(0, targetMs);
    }

    /// <summary>Spotify's position for a state, extrapolated to now when playing.</summary>
    public static long ProjectProgressMs(PlaybackStatePayload state, long localNow)
    {
        if (!state.IsPlaying)
        {
            return state.ProgressMs;
        }

        long elapsed = Math.Max(0, localNow - state.TimestampUtc.ToUnixTimeMilliseconds());
        return state.ProgressMs + elapsed;
    }

    /// <summary>
    /// While the seek is unconfirmed, a poll whose position disagrees with it
    /// is stale: its timeline is replaced by the optimistic one and everything
    /// else (device, volume, shuffle) is kept. Once confirmed, a disagreeing
    /// poll means playback moved elsewhere, so the display floor is dropped.
    /// </summary>
    private PlaybackStatePayload ReconcileWithOptimisticSeek(
        PlaybackStatePayload incoming,
        PlaybackStatePayload? expected,
        long localNow)
    {
        if (expected?.CurrentTrack is null || incoming.CurrentTrack?.Id != expected.CurrentTrack.Id)
        {
            Clear();
            return incoming;
        }

        long disagreementMs = Math.Abs(ProjectProgressMs(incoming, localNow) - ProjectProgressMs(expected, localNow));
        bool agrees = disagreementMs <= SEEK_CONFIRM_TOLERANCE_MS;

        if (_pendingSeekTrackId is not null)
        {
            if (agrees)
            {
                _pendingSeekTrackId = null;
                return incoming;
            }

            if (localNow - _pendingSeekIssuedAtMs < SEEK_CONFIRM_TIMEOUT_MS)
            {
                return CopyWithProgress(incoming, expected.ProgressMs, expected.IsPlaying, expected.TimestampUtc);
            }

            Clear();
            return incoming;
        }

        if (!agrees)
        {
            Clear();
        }

        return incoming;
    }

    private static PlaybackStatePayload CopyWithProgress(
        PlaybackStatePayload source,
        long progressMs,
        bool isPlaying,
        DateTimeOffset timestampUtc)
    {
        return new PlaybackStatePayload
        {
            CurrentTrack = source.CurrentTrack,
            ProgressMs = progressMs,
            IsPlaying = isPlaying,
            TimestampUtc = timestampUtc,
            DeviceName = source.DeviceName,
            VolumePercent = source.VolumePercent,
            IsShuffled = source.IsShuffled,
            RepeatMode = source.RepeatMode,
            ActiveUserId = source.ActiveUserId,
            ActiveUserDisplayName = source.ActiveUserDisplayName,
        };
    }
}
