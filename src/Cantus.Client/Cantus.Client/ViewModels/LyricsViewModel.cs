using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Core.Models;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

public sealed class LyricsViewModel : INotifyPropertyChanged
{
    private readonly SignalRPlaybackClient _client;
    private readonly DispatcherTimer _ticker;
    private readonly ThemeManager _themeManager;

    private PlaybackStatePayload? _lastPlaybackState;
    private LyricsPayload? _lastLyrics;
    private int _userOffsetMs;
    private int _activeLineIndex = -1;
    private long _interpolatedProgressMs;

    private string _connectionStatus = "Connecting...";
    private long _rttMs;
    private long _clockSkewMs;
    private string _pollerStatus = "Idle";
    private string _activeUserName = "None";
    private string? _activeUserId;
    private int _connectedClients = 1;
    private int _authorizedSessionsCount;
    private int _activePollIntervalMs = 1500;
    private string _transportType = "WebSockets";

    private string _currentTitle = "No Track Playing";
    private string _currentArtist = "Play music on Spotify to begin";
    private string _currentAlbum = string.Empty;
    private string? _albumArtUrl;
    private bool _isPlaying;
    private string _deviceName = "Spotify";
    private int? _volumePercent;

    private double _progressFraction;
    private string _progressText = "00:00";
    private string _totalDurationText = "00:00";
    private string _offsetText = "+0.0s";
    private bool _hasLyrics;
    private bool _isInstrumental;
    private bool _isInstrumentalBreak;
    private string _instrumentalBreakText = string.Empty;
    private bool _isKioskMode;

    public ObservableCollection<LyricLineViewModel> LyricLines { get; } = new();
    public ObservableCollection<AuthorizedSessionPayload> Sessions { get; } = new();
    public ThemeManager Theme => _themeManager;

    public ThemeMode SelectedThemeMode
    {
        get => _themeManager.CurrentMode;
        set
        {
            if (_themeManager.CurrentMode != value)
            {
                _themeManager.SetThemeMode(value);
                OnPropertyChanged();
            }
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set { if (_connectionStatus != value) { _connectionStatus = value; OnPropertyChanged(); } }
    }

    public long RttMs
    {
        get => _rttMs;
        set { if (_rttMs != value) { _rttMs = value; OnPropertyChanged(); } }
    }

    public long ClockSkewMs
    {
        get => _clockSkewMs;
        set { if (_clockSkewMs != value) { _clockSkewMs = value; OnPropertyChanged(); } }
    }

    public string PollerStatus
    {
        get => _pollerStatus;
        set { if (_pollerStatus != value) { _pollerStatus = value; OnPropertyChanged(); } }
    }

    public string ActiveUserName
    {
        get => _activeUserName;
        set { if (_activeUserName != value) { _activeUserName = value; OnPropertyChanged(); } }
    }

    public string? ActiveUserId
    {
        get => _activeUserId;
        set { if (_activeUserId != value) { _activeUserId = value; OnPropertyChanged(); } }
    }

    public int ConnectedClients
    {
        get => _connectedClients;
        set { if (_connectedClients != value) { _connectedClients = value; OnPropertyChanged(); } }
    }

    public int AuthorizedSessionsCount
    {
        get => _authorizedSessionsCount;
        set { if (_authorizedSessionsCount != value) { _authorizedSessionsCount = value; OnPropertyChanged(); } }
    }

    public int ActivePollIntervalMs
    {
        get => _activePollIntervalMs;
        set { if (_activePollIntervalMs != value) { _activePollIntervalMs = value; OnPropertyChanged(); } }
    }

    public string TransportType
    {
        get => _transportType;
        set { if (_transportType != value) { _transportType = value; OnPropertyChanged(); } }
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        set { if (_currentTitle != value) { _currentTitle = value; OnPropertyChanged(); } }
    }

    public string CurrentArtist
    {
        get => _currentArtist;
        set { if (_currentArtist != value) { _currentArtist = value; OnPropertyChanged(); } }
    }

    public string CurrentAlbum
    {
        get => _currentAlbum;
        set { if (_currentAlbum != value) { _currentAlbum = value; OnPropertyChanged(); } }
    }

    public string? AlbumArtUrl
    {
        get => _albumArtUrl;
        set { if (_albumArtUrl != value) { _albumArtUrl = value; OnPropertyChanged(); } }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set { if (_isPlaying != value) { _isPlaying = value; OnPropertyChanged(); } }
    }

    public string DeviceName
    {
        get => _deviceName;
        set { if (_deviceName != value) { _deviceName = value; OnPropertyChanged(); } }
    }

    public int? VolumePercent
    {
        get => _volumePercent;
        set { if (_volumePercent != value) { _volumePercent = value; OnPropertyChanged(); } }
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        set { if (Math.Abs(_progressFraction - value) > 0.0005) { _progressFraction = value; OnPropertyChanged(); } }
    }

    public string ProgressText
    {
        get => _progressText;
        set { if (_progressText != value) { _progressText = value; OnPropertyChanged(); } }
    }

    public string TotalDurationText
    {
        get => _totalDurationText;
        set { if (_totalDurationText != value) { _totalDurationText = value; OnPropertyChanged(); } }
    }

    public string OffsetText
    {
        get => _offsetText;
        set { if (_offsetText != value) { _offsetText = value; OnPropertyChanged(); } }
    }

    public bool HasLyrics
    {
        get => _hasLyrics;
        set { if (_hasLyrics != value) { _hasLyrics = value; OnPropertyChanged(); } }
    }

    public bool IsInstrumental
    {
        get => _isInstrumental;
        set { if (_isInstrumental != value) { _isInstrumental = value; OnPropertyChanged(); } }
    }

    public bool IsInstrumentalBreak
    {
        get => _isInstrumentalBreak;
        set { if (_isInstrumentalBreak != value) { _isInstrumentalBreak = value; OnPropertyChanged(); } }
    }

    public string InstrumentalBreakText
    {
        get => _instrumentalBreakText;
        set { if (_instrumentalBreakText != value) { _instrumentalBreakText = value; OnPropertyChanged(); } }
    }

    public bool IsKioskMode
    {
        get => _isKioskMode;
        set { if (_isKioskMode != value) { _isKioskMode = value; OnPropertyChanged(); } }
    }

    public int ActiveLineIndex
    {
        get => _activeLineIndex;
        private set
        {
            if (_activeLineIndex != value)
            {
                _activeLineIndex = value;
                OnPropertyChanged();
                UpdateLyricLineStates(value);
            }
        }
    }

    public event Action<int>? ActiveLineChanged;

    public LyricsViewModel(SignalRPlaybackClient client, ThemeManager? themeManager = null)
    {
        _client = client;
        _themeManager = themeManager ?? ThemeManager.Instance;

        _client.ConnectionStateChanged += state => ConnectionStatus = state;
        _client.PlaybackStateReceived += OnPlaybackStateReceived;
        _client.LyricsReceived += OnLyricsReceived;
        _client.TrackOffsetReceived += OnTrackOffsetReceived;
        _client.SessionsReceived += OnSessionsReceived;
        _client.DiagnosticsReceived += OnDiagnosticsReceived;

        _ticker = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _ticker.Tick += OnTick;
        _ticker.Start();
    }

    public async Task InitializeAsync()
    {
        await _client.StartAsync();
    }

    public void ToggleKioskMode()
    {
        IsKioskMode = !IsKioskMode;
    }

    private void OnPlaybackStateReceived(PlaybackStatePayload state)
    {
        _lastPlaybackState = state;
        RttMs = _client.RttMs;
        ClockSkewMs = _client.ClockOffsetMs;
        TransportType = _client.TransportType;

        if (state.CurrentTrack is null)
        {
            CurrentTitle = "No Track Playing";
            CurrentArtist = "Play music on Spotify to begin";
            CurrentAlbum = string.Empty;
            AlbumArtUrl = null;
            IsPlaying = false;
            DeviceName = "No Device";
            VolumePercent = null;
            _themeManager.UpdateTrackMetadata(null, null, null);
            return;
        }

        var track = state.CurrentTrack;
        CurrentTitle = track.Title;
        CurrentArtist = track.Artist;
        CurrentAlbum = track.Album ?? string.Empty;
        AlbumArtUrl = track.AlbumArtUrl;
        IsPlaying = state.IsPlaying;
        DeviceName = state.DeviceName ?? "Spotify";
        VolumePercent = state.VolumePercent;
        if (!string.IsNullOrEmpty(state.ActiveUserDisplayName))
        {
            ActiveUserName = state.ActiveUserDisplayName;
        }
        ActiveUserId = state.ActiveUserId;

        // Update Theme Manager for Dynamic Palette Sampling
        _themeManager.UpdateTrackMetadata(track.Title, track.Artist, track.AlbumArtUrl);

        // Drift check & snap
        long targetMs = state.ProgressMs;
        if (state.IsPlaying)
        {
            long serverTimestamp = state.TimestampUtc.ToUnixTimeMilliseconds();
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _client.ClockOffsetMs;
            long elapsed = Math.Max(0, localNow - serverTimestamp);
            targetMs += elapsed;
        }

        long drift = Math.Abs(_interpolatedProgressMs - targetMs);
        if (drift > 1500 || !_isPlaying)
        {
            _interpolatedProgressMs = targetMs;
        }
    }

    private void OnLyricsReceived(LyricsPayload lyrics)
    {
        _lastLyrics = lyrics;
        HasLyrics = lyrics.Lines.Count > 0;
        IsInstrumental = lyrics.IsInstrumental;

        LyricLines.Clear();
        foreach (var line in lyrics.Lines)
        {
            LyricLines.Add(new LyricLineViewModel
            {
                TimestampMs = line.TimestampMs,
                Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text
            });
        }
        ActiveLineIndex = -1;
    }

    private void OnTrackOffsetReceived(TrackOffsetPayload offset)
    {
        if (_lastPlaybackState?.CurrentTrack?.Id == offset.TrackId)
        {
            _userOffsetMs = offset.OffsetMs;
            double seconds = _userOffsetMs / 1000.0;
            OffsetText = $"{(_userOffsetMs >= 0 ? "+" : "")}{seconds:0.0}s";
        }
    }

    private void OnSessionsReceived(IReadOnlyList<AuthorizedSessionPayload> sessions)
    {
        Sessions.Clear();
        foreach (var s in sessions)
        {
            Sessions.Add(s);
        }
        AuthorizedSessionsCount = sessions.Count;
    }

    private void OnDiagnosticsReceived(DiagnosticsPayload diag)
    {
        PollerStatus = diag.PollerStatus;
        ConnectedClients = diag.ConnectedClients;
        AuthorizedSessionsCount = diag.AuthorizedSessions;
        ActivePollIntervalMs = diag.ActivePollIntervalMs;
        if (!string.IsNullOrEmpty(diag.ActiveUserName))
        {
            ActiveUserName = diag.ActiveUserName;
        }
        ActiveUserId = diag.ActiveUserId;
    }

    private void OnTick(object? sender, object e)
    {
        if (_lastPlaybackState?.CurrentTrack is null)
        {
            ProgressFraction = 0;
            ProgressText = "00:00";
            TotalDurationText = "00:00";
            IsInstrumentalBreak = false;
            return;
        }

        var track = _lastPlaybackState.CurrentTrack;
        long durationMs = (long)track.Duration.TotalMilliseconds;
        if (durationMs <= 0) durationMs = 1;

        long targetMs = _lastPlaybackState.ProgressMs;
        if (_lastPlaybackState.IsPlaying)
        {
            long serverTimestamp = _lastPlaybackState.TimestampUtc.ToUnixTimeMilliseconds();
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _client.ClockOffsetMs;
            long elapsed = Math.Max(0, localNow - serverTimestamp);
            targetMs += elapsed;

            // Apply smooth drift slew filter towards target
            long delta = targetMs - _interpolatedProgressMs;
            if (Math.Abs(delta) > 1500)
            {
                _interpolatedProgressMs = targetMs;
            }
            else
            {
                _interpolatedProgressMs += (long)Math.Round(delta * 0.20);
            }
        }
        else
        {
            _interpolatedProgressMs = targetMs;
        }

        long currentWithOffset = Math.Clamp(_interpolatedProgressMs + _userOffsetMs, 0, durationMs);

        ProgressFraction = (double)currentWithOffset / durationMs;
        ProgressText = FormatTime(currentWithOffset);
        TotalDurationText = FormatTime(durationMs);

        if (LyricLines.Count > 0)
        {
            int idx = FindActiveLineIndex(currentWithOffset);
            if (idx != ActiveLineIndex)
            {
                ActiveLineIndex = idx;
                ActiveLineChanged?.Invoke(idx);
            }

            // Check for Instrumental Break
            EvaluateInstrumentalBreak(idx, currentWithOffset);
        }
        else
        {
            IsInstrumentalBreak = false;
        }
    }

    private void EvaluateInstrumentalBreak(int activeIdx, long currentMs)
    {
        if (LyricLines.Count == 0)
        {
            IsInstrumentalBreak = false;
            return;
        }

        long nextTimestampMs = 0;
        bool hasNextLine = false;

        if (activeIdx < 0)
        {
            // Intro before first line
            nextTimestampMs = LyricLines[0].TimestampMs;
            hasNextLine = true;
        }
        else if (activeIdx < LyricLines.Count - 1)
        {
            nextTimestampMs = LyricLines[activeIdx + 1].TimestampMs;
            long currentLineTimestamp = LyricLines[activeIdx].TimestampMs;
            if (nextTimestampMs - currentLineTimestamp >= 8000)
            {
                hasNextLine = true;
            }
        }

        if (hasNextLine && nextTimestampMs > currentMs)
        {
            long remainingMs = nextTimestampMs - currentMs;
            if (remainingMs >= 3000)
            {
                IsInstrumentalBreak = true;
                int remainingSeconds = (int)Math.Ceiling(remainingMs / 1000.0);
                InstrumentalBreakText = $"♪ Instrumental Interlude ({remainingSeconds:D2}s) ♪";
                return;
            }
        }

        IsInstrumentalBreak = false;
    }

    public int FindActiveLineIndex(long currentMs)
    {
        if (LyricLines.Count == 0) return -1;

        int low = 0;
        int high = LyricLines.Count - 1;
        int result = -1;

        while (low <= high)
        {
            int mid = (low + high) >> 1;
            if (LyricLines[mid].TimestampMs <= currentMs)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    private void UpdateLyricLineStates(int activeIdx)
    {
        for (int i = 0; i < LyricLines.Count; i++)
        {
            LyricLines[i].IsActive = (i == activeIdx);
            LyricLines[i].IsPast = (i < activeIdx);
        }
    }

    public async Task NudgeOffsetAsync(int deltaMs)
    {
        if (_lastPlaybackState?.CurrentTrack is null) return;
        _userOffsetMs += deltaMs;
        double seconds = _userOffsetMs / 1000.0;
        OffsetText = $"{(_userOffsetMs >= 0 ? "+" : "")}{seconds:0.1}s";
        await _client.SetTrackOffsetAsync(_lastPlaybackState.CurrentTrack.Id, _userOffsetMs);
    }

    public async Task ResetOffsetAsync()
    {
        if (_lastPlaybackState?.CurrentTrack is null) return;
        _userOffsetMs = 0;
        OffsetText = "+0.0s";
        await _client.SetTrackOffsetAsync(_lastPlaybackState.CurrentTrack.Id, 0);
    }

    public async Task SubscribeToUserAsync(string? userId)
    {
        await _client.SubscribeToUserAsync(userId);
    }

    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
