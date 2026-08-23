using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Clock;

public sealed class PlaybackInterpolator : IPlaybackInterpolator
{
    private readonly TimeProvider _timeProvider;
    private readonly PlaybackInterpolatorOptions _options;
    private string? _lastTrackId;
    private TimeSpan? _lastPosition;
    private DateTimeOffset? _lastCalculationTime;

    public PlaybackInterpolator(
        TimeProvider? timeProvider = null,
        IOptions<PlaybackInterpolatorOptions>? options = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options?.Value ?? new PlaybackInterpolatorOptions();
    }

    public TimeSpan CalculateCurrentPosition(PlaybackState? state, TimeSpan userOffset)
    {
        if (state is null || state.CurrentTrack is null)
        {
            Reset();
            return TimeSpan.Zero;
        }

        var now = _timeProvider.GetUtcNow();

        // If track changed, reset interpolator history
        if (_lastTrackId != state.CurrentTrack.Id)
        {
            _lastTrackId = state.CurrentTrack.Id;
            _lastPosition = null;
            _lastCalculationTime = null;
        }

        if (!state.IsPlaying)
        {
            var pausedPosition = state.Progress + userOffset;
            if (pausedPosition < TimeSpan.Zero)
            {
                pausedPosition = TimeSpan.Zero;
            }

            _lastPosition = pausedPosition;
            _lastCalculationTime = now;
            return pausedPosition;
        }

        var elapsedSinceSnapshot = now - state.TimestampUtc;
        if (elapsedSinceSnapshot < TimeSpan.Zero)
        {
            elapsedSinceSnapshot = TimeSpan.Zero;
        }

        var targetPosition = state.Progress + elapsedSinceSnapshot + userOffset;
        if (targetPosition < TimeSpan.Zero)
        {
            targetPosition = TimeSpan.Zero;
        }

        // Clamp to track duration if known
        if (state.CurrentTrack.Duration > TimeSpan.Zero && targetPosition > state.CurrentTrack.Duration)
        {
            targetPosition = state.CurrentTrack.Duration;
        }

        // First calculation after reset
        if (_lastPosition is null || _lastCalculationTime is null)
        {
            _lastPosition = targetPosition;
            _lastCalculationTime = now;
            return targetPosition;
        }

        var delta = (targetPosition - _lastPosition.Value).Duration();
        var seekThreshold = TimeSpan.FromMilliseconds(_options.SeekThresholdMs);

        // If delta exceeds seek threshold, user likely seeked/skipped -> snap directly
        if (delta > seekThreshold)
        {
            _lastPosition = targetPosition;
            _lastCalculationTime = now;
            return targetPosition;
        }

        // Progressive smooth clock advance
        var localElapsed = now - _lastCalculationTime.Value;
        if (localElapsed < TimeSpan.Zero)
        {
            localElapsed = TimeSpan.Zero;
        }

        var advancedPosition = _lastPosition.Value + localElapsed;
        var drift = (targetPosition - advancedPosition).Duration();
        var driftTolerance = TimeSpan.FromMilliseconds(_options.DriftToleranceMs);

        if (drift > driftTolerance)
        {
            // Nudge towards target position
            var corrected = advancedPosition + TimeSpan.FromMilliseconds((targetPosition - advancedPosition).TotalMilliseconds * 0.2);
            _lastPosition = corrected;
        }
        else
        {
            _lastPosition = advancedPosition;
        }

        _lastCalculationTime = now;
        return _lastPosition.Value;
    }

    public void Reset()
    {
        _lastTrackId = null;
        _lastPosition = null;
        _lastCalculationTime = null;
    }
}
