using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cantus.Client.Services;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Where lyrics render relative to Spotify's position: the device latency
/// compensation (persisted per device) and the per-track offset (persisted
/// server-side per track ID). Composes them into the render position
/// <c>playback position - latency compensation + track offset</c>. The latency
/// term applies to the display clock's target so the clock slews when it
/// changes; the track offset applies on top of the interpolated position.
/// </summary>
public sealed class PlaybackTiming : INotifyPropertyChanged
{
    /// <summary>Step for the [ and ] keys and the settings steppers.</summary>
    public const int LATENCY_STEP_MS = 50;

    public const int MIN_TRACK_OFFSET_MS = -30000;
    public const int MAX_TRACK_OFFSET_MS = 30000;

    private const double MS_PER_SECOND = 1000.0;
    private const string ZERO_OFFSET_TEXT = "+0.0s";

    private readonly ISignalRPlaybackClient _client;
    private readonly IClientPreferences _preferences;
    private readonly Func<string?> _currentTrackId;

    private int _trackOffsetMs;
    private string _offsetText = ZERO_OFFSET_TEXT;
    private int _latencyCompensationMs;

    /// <param name="currentTrackId">The playing track's ID, or null when nothing plays.</param>
    public PlaybackTiming(ISignalRPlaybackClient client, IClientPreferences preferences, Func<string?> currentTrackId)
    {
        _client = client;
        _preferences = preferences;
        _currentTrackId = currentTrackId;
        _latencyCompensationMs = _preferences.LatencyCompensationMs;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The current track's offset. Positive values render lyrics earlier.</summary>
    public int TrackOffsetMs
    {
        get => _trackOffsetMs;
        private set
        {
            if (_trackOffsetMs != value)
            {
                _trackOffsetMs = value;
                OnPropertyChanged();
            }
        }
    }

    public string OffsetText
    {
        get => _offsetText;
        private set
        {
            if (_offsetText != value)
            {
                _offsetText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Compensates for the delay between a position estimate and the audio
    /// reaching the listener (Bluetooth speakers, TV audio chains). Clamped to
    /// the supported range and persisted on every change.
    /// </summary>
    public int LatencyCompensationMs
    {
        get => _latencyCompensationMs;
        internal set
        {
            int clamped = ClientPreferences.ClampLatency(value);
            if (_latencyCompensationMs != clamped)
            {
                _latencyCompensationMs = clamped;
                _preferences.LatencyCompensationMs = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalibrationText));
            }
        }
    }

    public string CalibrationText => $"{_latencyCompensationMs} ms";

    /// <summary>The position the display clock interpolates toward: Spotify's projected position minus latency compensation.</summary>
    public long ComputeClockTargetMs(long projectedProgressMs) =>
        Math.Max(0, projectedProgressMs - _latencyCompensationMs);

    /// <summary>The position lyrics render at: the display clock plus the track offset, kept within the track.</summary>
    public long ComputeRenderPositionMs(long clockPositionMs, long durationMs) =>
        Math.Clamp(clockPositionMs + _trackOffsetMs, 0, durationMs);

    /// <summary>
    /// Adjusts the device latency compensation. Positive deltas render lyrics
    /// later; negative deltas render them earlier (the fix when lyrics trail
    /// the audio).
    /// </summary>
    public void AdjustLatencyCompensation(int deltaMs)
    {
        LatencyCompensationMs = _latencyCompensationMs + deltaMs;
    }

    public async Task NudgeOffsetAsync(int deltaMs)
    {
        string? trackId = _currentTrackId();
        if (trackId is null)
        {
            return;
        }

        TrackOffsetMs = Math.Clamp(_trackOffsetMs + deltaMs, MIN_TRACK_OFFSET_MS, MAX_TRACK_OFFSET_MS);
        OffsetText = FormatOffset(_trackOffsetMs);
        await _client.SetTrackOffsetAsync(trackId, _trackOffsetMs);
    }

    public async Task ResetOffsetAsync()
    {
        string? trackId = _currentTrackId();
        if (trackId is null)
        {
            return;
        }

        TrackOffsetMs = 0;
        OffsetText = ZERO_OFFSET_TEXT;
        await _client.SetTrackOffsetAsync(trackId, 0);
    }

    /// <summary>Saves an offset for the current track. The local offset follows when the server echoes it back.</summary>
    public async Task SetSyncOffsetAsync(int offsetMs)
    {
        if (_currentTrackId() is string trackId)
        {
            await _client.SetTrackOffsetAsync(trackId, offsetMs);
        }
    }

    /// <summary>Takes a saved offset from the server. Offsets for any track but the current one are ignored.</summary>
    public void ApplyTrackOffset(TrackOffsetPayload? offset)
    {
        if (offset is null)
        {
            return;
        }

        if (_currentTrackId() == offset.TrackId)
        {
            TrackOffsetMs = offset.OffsetMs;
            OffsetText = FormatOffset(_trackOffsetMs);
        }
    }

    private static string FormatOffset(int offsetMs)
    {
        double seconds = offsetMs / MS_PER_SECOND;
        return $"{(offsetMs >= 0 ? "+" : "")}{seconds:0.0}s";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
