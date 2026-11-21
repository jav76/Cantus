using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Core.Models;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Spotify transport controls (play/pause, skip, shuffle, repeat, volume, seek)
/// and the transport status line. Owns the effective playback state: the last
/// server poll, reconciled through an <see cref="OptimisticSeekAnchor"/> with
/// any seek Spotify has not confirmed yet.
/// </summary>
public sealed class PlaybackTransport : INotifyPropertyChanged
{
    private const int TRANSPORT_STATUS_DURATION_MS = 8000;
    private const int VOLUME_DEBOUNCE_MS = 300;
    private const string REPEAT_OFF = "off";
    private const string REPEAT_CONTEXT = "context";
    private const string REPEAT_TRACK = "track";

    internal const string MISSING_PERMISSIONS_TEXT =
        "Spotify rejected the command. Log out and reconnect your account to grant playback control (Premium required).";
    internal const string NO_ACTIVE_DEVICE_TEXT =
        "No active Spotify device. Start playback in any Spotify app first.";
    internal const string SPOTIFY_UNREACHABLE_TEXT = "Couldn't reach Spotify. Try again.";

    private readonly ISignalRPlaybackClient _client;
    private readonly ThemeManager _themeManager;
    private readonly DispatcherTimer _transportStatusTimer;
    private readonly DispatcherTimer _volumeDebounceTimer;
    private readonly OptimisticSeekAnchor _seekAnchor = new();

    private bool _isPlaying;
    private bool _isShuffled;
    private string _repeatMode = REPEAT_OFF;
    private int? _volumePercent;
    private int _pendingVolumePercent;
    private string _transportStatusText = string.Empty;

    public PlaybackTransport(ISignalRPlaybackClient client, ThemeManager themeManager)
    {
        _client = client;
        _themeManager = themeManager;
        _themeManager.PaletteChanged += OnPaletteChanged;

        _transportStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TRANSPORT_STATUS_DURATION_MS)
        };
        _transportStatusTimer.Tick += (s, e) => HideTransportStatus();

        _volumeDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(VOLUME_DEBOUNCE_MS)
        };
        _volumeDebounceTimer.Tick += OnVolumeDebounceTick;
    }

    /// <summary>
    /// The playback state the display clock follows: the last poll, or an
    /// optimistic copy of it while a line seek is unconfirmed.
    /// </summary>
    public PlaybackStatePayload? PlaybackState { get; private set; }

    /// <summary>Raised with the server-clock time after a line seek replaces the timeline.</summary>
    public event Action<long>? SeekApplied;

    /// <summary>Raised with the server-clock time after a rejected seek restores the last poll.</summary>
    public event Action<long>? SeekReverted;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public string PlayPauseGlyph => IsPlaying ? "\u23F8" : "\u25B6";

    public bool IsShuffled
    {
        get => _isShuffled;
        set
        {
            if (_isShuffled != value)
            {
                _isShuffled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShuffleButtonForeground));
            }
        }
    }

    /// <summary>Spotify repeat state: "off", "track", or "context".</summary>
    public string RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode != value)
            {
                _repeatMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RepeatGlyph));
                OnPropertyChanged(nameof(RepeatButtonForeground));
            }
        }
    }

    public string RepeatGlyph => _repeatMode == REPEAT_TRACK ? "\uE8ED" : "\uE8EE";

    public Microsoft.UI.Xaml.Media.SolidColorBrush ShuffleButtonForeground =>
        _isShuffled ? _themeManager.PrimaryAccentBrush : _themeManager.TextMutedBrush;

    public Microsoft.UI.Xaml.Media.SolidColorBrush RepeatButtonForeground =>
        _repeatMode != REPEAT_OFF ? _themeManager.PrimaryAccentBrush : _themeManager.TextMutedBrush;

    public int? VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (_volumePercent != value)
            {
                _volumePercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeSliderValue));
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public double VolumeSliderValue => _volumePercent ?? 0;

    public string VolumeText => _volumePercent is int v ? $"{v}%" : "\u2014";

    public string TransportStatusText => _transportStatusText;

    public Visibility TransportStatusVisibility =>
        string.IsNullOrEmpty(_transportStatusText) ? Visibility.Collapsed : Visibility.Visible;

    public long GetServerNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _client.ClockOffsetMs;

    /// <summary>
    /// Takes a server poll: reconciles it with any optimistic seek, stores the
    /// result as <see cref="PlaybackState"/> and copies its transport fields.
    /// Returns the effective state the display clock should follow.
    /// </summary>
    public PlaybackStatePayload ApplyServerState(PlaybackStatePayload state, long localNow)
    {
        state = _seekAnchor.Reconcile(state, PlaybackState, localNow);
        PlaybackState = state;

        if (state.CurrentTrack is null)
        {
            IsPlaying = false;
            VolumePercent = null;
            IsShuffled = false;
            RepeatMode = REPEAT_OFF;
        }
        else
        {
            IsPlaying = state.IsPlaying;
            VolumePercent = state.VolumePercent;
            IsShuffled = state.IsShuffled;
            RepeatMode = state.RepeatMode;
        }

        return state;
    }

    /// <summary>Forgets all playback state and any pending seek, as after logging out.</summary>
    public void Reset()
    {
        PlaybackState = null;
        _seekAnchor.Reset();
    }

    public void ClearSeekAnchor() => _seekAnchor.Clear();

    /// <inheritdoc cref="OptimisticSeekAnchor.HoldAtFloor"/>
    public long HoldAtSeekFloor(long displayPositionMs) => _seekAnchor.HoldAtFloor(displayPositionMs);

    /// <summary>
    /// Sends pause when playing and resume when paused. On success the playing
    /// flag flips optimistically; the command also triggers server polls, so
    /// the authoritative state arrives within a second.
    /// </summary>
    public async Task TogglePlayPauseAsync()
    {
        if (PlaybackState?.CurrentTrack is null)
        {
            return;
        }

        string command = IsPlaying ? "pause" : "resume";
        PlayerCommandResult result = await _client.SendPlayerCommandAsync(command);
        if (result == PlayerCommandResult.Success)
        {
            IsPlaying = !IsPlaying;
        }

        ReportTransportResult(result);
    }

    public async Task SkipToNextAsync()
    {
        if (PlaybackState?.CurrentTrack is null)
        {
            return;
        }

        ReportTransportResult(await _client.SendPlayerCommandAsync("next"));
    }

    public async Task SkipToPreviousAsync()
    {
        if (PlaybackState?.CurrentTrack is null)
        {
            return;
        }

        ReportTransportResult(await _client.SendPlayerCommandAsync("previous"));
    }

    public async Task ToggleShuffleAsync()
    {
        if (PlaybackState?.CurrentTrack is null)
        {
            return;
        }

        bool target = !IsShuffled;
        PlayerCommandResult result = await _client.SetShuffleAsync(target);
        if (result == PlayerCommandResult.Success)
        {
            IsShuffled = target;
        }

        ReportTransportResult(result);
    }

    /// <summary>Cycles repeat the way Spotify's own client does: off, then all ("context"), then one ("track").</summary>
    public async Task CycleRepeatAsync()
    {
        if (PlaybackState?.CurrentTrack is null)
        {
            return;
        }

        string next = RepeatMode switch
        {
            REPEAT_OFF => REPEAT_CONTEXT,
            REPEAT_CONTEXT => REPEAT_TRACK,
            _ => REPEAT_OFF
        };

        PlayerCommandResult result = await _client.SetRepeatAsync(next);
        if (result == PlayerCommandResult.Success)
        {
            RepeatMode = next;
        }

        ReportTransportResult(result);
    }

    public async Task SeekToFractionAsync(double fraction)
    {
        TrackInfoPayload? track = PlaybackState?.CurrentTrack;
        if (track is null || track.DurationMs <= 0)
        {
            return;
        }

        ClearSeekAnchor();
        double clamped = Math.Clamp(fraction, 0.0, 1.0);
        long positionMs = (long)(clamped * track.DurationMs);
        ReportTransportResult(await _client.SeekPlaybackAsync(positionMs));
    }

    /// <summary>
    /// Seeks playback to the moment a synced line is sung. The timeline jumps
    /// to the line and <see cref="SeekApplied"/> fires before Spotify answers;
    /// a rejected seek restores the last poll and fires <see cref="SeekReverted"/>.
    /// </summary>
    public async Task SeekToLineAsync(long lineTimestampMs, int userOffsetMs)
    {
        PlaybackStatePayload? state = PlaybackState;
        if (state?.CurrentTrack is null)
        {
            return;
        }

        TrackInfoPayload track = state.CurrentTrack;
        long targetMs = OptimisticSeekAnchor.ComputeLineSeekTargetMs(lineTimestampMs, userOffsetMs, track.DurationMs);
        long localNow = GetServerNowMs();

        PlaybackState = _seekAnchor.Begin(state, targetMs, IsPlaying, localNow, lineTimestampMs);
        SeekApplied?.Invoke(localNow);

        int generation = _seekAnchor.NextGeneration();
        PlayerCommandResult result = await _client.SeekPlaybackAsync(targetMs);
        if (result != PlayerCommandResult.Success && _seekAnchor.IsCurrentGeneration(generation))
        {
            RevertOptimisticSeek();
        }

        ReportTransportResult(result);
    }

    /// <summary>
    /// Called for every slider tick while the user drags, so the actual
    /// Spotify call is debounced: only the value the slider rests on is sent.
    /// </summary>
    public void RequestVolumeChange(int volumePercent)
    {
        _pendingVolumePercent = Math.Clamp(volumePercent, 0, 100);
        _volumeDebounceTimer.Stop();
        _volumeDebounceTimer.Start();
    }

    internal async Task SendPendingVolumeAsync()
    {
        _volumeDebounceTimer.Stop();
        if (PlaybackState?.CurrentTrack is null)
        {
            return;
        }

        PlayerCommandResult result = await _client.SetPlayerVolumeAsync(_pendingVolumePercent);
        if (result == PlayerCommandResult.Success)
        {
            VolumePercent = _pendingVolumePercent;
        }

        ReportTransportResult(result);
    }

    /// <summary>
    /// Surfaces why a transport command was rejected. The 403 case matters
    /// most: accounts linked before the user-modify-playback-state scope was
    /// added fail silently until they are re-linked, and nothing else tells the
    /// user that.
    /// </summary>
    internal void ReportTransportResult(PlayerCommandResult result)
    {
        string message = result switch
        {
            PlayerCommandResult.MissingPermissions => MISSING_PERMISSIONS_TEXT,
            PlayerCommandResult.NoActiveDevice => NO_ACTIVE_DEVICE_TEXT,
            PlayerCommandResult.Failed => SPOTIFY_UNREACHABLE_TEXT,
            _ => string.Empty
        };

        if (_transportStatusText != message)
        {
            _transportStatusText = message;
            OnPropertyChanged(nameof(TransportStatusText));
            OnPropertyChanged(nameof(TransportStatusVisibility));
        }

        _transportStatusTimer.Stop();
        if (message.Length > 0)
        {
            _transportStatusTimer.Start();
        }
    }

    private void HideTransportStatus()
    {
        _transportStatusTimer.Stop();
        if (_transportStatusText.Length > 0)
        {
            _transportStatusText = string.Empty;
            OnPropertyChanged(nameof(TransportStatusText));
            OnPropertyChanged(nameof(TransportStatusVisibility));
        }
    }

    private async void OnVolumeDebounceTick(object? sender, object e)
    {
        await SendPendingVolumeAsync();
    }

    private void OnPaletteChanged(ColorPalette palette)
    {
        OnPropertyChanged(nameof(ShuffleButtonForeground));
        OnPropertyChanged(nameof(RepeatButtonForeground));
    }

    private void RevertOptimisticSeek()
    {
        PlaybackStatePayload? serverState = _seekAnchor.Revert();
        if (serverState is null)
        {
            return;
        }

        PlaybackState = serverState;
        SeekReverted?.Invoke(GetServerNowMs());
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
