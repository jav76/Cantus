using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
    private readonly ResponsiveLayoutManager _layoutManager;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    private PlaybackStatePayload? _lastPlaybackState;
    private LyricsPayload? _lastLyrics;
    private int _userOffsetMs;
    private int _activeLineIndex = -1;
    private long _interpolatedProgressMs;
    private long _lastTickTimestampMs;
    private const int DefaultAcousticLatencyCompensationMs = 200;

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
    private bool _isCalibrationMode;
    private bool _isStaticLyricsMode;
    private int? _previousOffsetMs;
    private string? _calibrationToastMessage;
    private bool _isCalibrationToastVisible;
    private DispatcherTimer? _toastDismissTimer;

    public ObservableCollection<LyricLineViewModel> LyricLines { get; } = new();
    public ObservableCollection<AuthorizedSessionPayload> Sessions { get; } = new();
    public ThemeManager Theme => _themeManager;
    public ResponsiveLayoutManager Layout => _layoutManager;

    // Flattened Theme Properties for 1-level safe XAML {x:Bind}
    public Microsoft.UI.Xaml.Media.SolidColorBrush BackgroundBrush => _themeManager.BackgroundBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush SurfaceCardBrush => _themeManager.SurfaceCardBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush CardBorderBrush => _themeManager.CardBorderBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush PrimaryAccentBrush => _themeManager.PrimaryAccentBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush SecondaryAccentBrush => _themeManager.SecondaryAccentBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush TextPrimaryBrush => _themeManager.TextPrimaryBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush TextSecondaryBrush => _themeManager.TextSecondaryBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush TextMutedBrush => _themeManager.TextMutedBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush GlowBrush => _themeManager.GlowBrush;
    public Windows.UI.Color ActivePrimaryAccentColor => _themeManager.ActivePalette.PrimaryAccent;
    public Windows.UI.Color ActiveBackgroundColor => _themeManager.ActivePalette.Background;

    // Flattened Layout Properties for 1-level safe XAML {x:Bind}
    public LayoutBreakpoint CurrentBreakpoint => _layoutManager.CurrentBreakpoint;
    public MobileViewMode MobileView => _layoutManager.MobileView;
    public double SidePanelWidth => _layoutManager.SidePanelWidth;
    public double AlbumArtSize => _layoutManager.AlbumArtSize;
    public double LyricsMaxWidth => _layoutManager.LyricsMaxWidth;
    public Thickness ContentPadding => _layoutManager.ContentPadding;

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
        set
        {
            if (_authorizedSessionsCount != value)
            {
                _authorizedSessionsCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NoSessionsVisibility));
                OnPropertyChanged(nameof(HasSessionsVisibility));
                OnPropertyChanged(nameof(IsAuthorized));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(ConnectButtonGlyph));
            }
        }
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

    public bool IsAuthorized =>
        AuthorizedSessionsCount > 0 ||
        (ActiveUserName != "None" && !string.IsNullOrEmpty(ActiveUserName));

    public AuthorizedSessionPayload? CurrentUserSession => Sessions.FirstOrDefault();

    public string ConnectButtonText => IsAuthorized
        ? (ActiveUserName != "None" && !string.IsNullOrEmpty(ActiveUserName)
            ? $"Connected: {ActiveUserName}"
            : "Connected")
        : "Connect Spotify";

    public string ConnectButtonGlyph => IsAuthorized ? "\uE73E" : "\uE8D6";

    public Visibility NoSessionsVisibility =>
        AuthorizedSessionsCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasSessionsVisibility =>
        AuthorizedSessionsCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsKioskMode
    {
        get => _isKioskMode;
        set
        {
            if (_isKioskMode != value)
            {
                _isKioskMode = value;
                _layoutManager.IsKioskMode = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCalibrationMode
    {
        get => _isCalibrationMode;
        set
        {
            if (_isCalibrationMode != value)
            {
                _isCalibrationMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalibrationModeVisibility));
                OnPropertyChanged(nameof(CalibrationButtonBrush));
                foreach (LyricLineViewModel line in LyricLines)
                {
                    line.IsCalibrationMode = value;
                }
            }
        }
    }

    public Visibility CalibrationModeVisibility => _isCalibrationMode ? Visibility.Visible : Visibility.Collapsed;
    public Microsoft.UI.Xaml.Media.SolidColorBrush CalibrationButtonBrush =>
        _isCalibrationMode ? PrimaryAccentBrush : TextPrimaryBrush;

    public bool IsStaticLyricsMode
    {
        get => _isStaticLyricsMode;
        set
        {
            if (_isStaticLyricsMode != value)
            {
                _isStaticLyricsMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SyncedLyricsVisibility));
                OnPropertyChanged(nameof(StaticLyricsVisibility));
                OnPropertyChanged(nameof(ModeToggleText));
                OnPropertyChanged(nameof(ModeToggleGlyph));
                OnPropertyChanged(nameof(StaticLyricsText));
            }
        }
    }

    public string StaticLyricsText => _lastLyrics?.PlainLyrics
        ?? (LyricLines.Count > 0
            ? string.Join("\n", LyricLines.Select(l => l.Text))
            : "No lyrics available.");

    public Visibility SyncedLyricsVisibility =>
        (!IsStaticLyricsMode && HasLyrics) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StaticLyricsVisibility =>
        (IsStaticLyricsMode && HasLyrics) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModeToggleVisibility => HasLyrics ? Visibility.Visible : Visibility.Collapsed;
    public string ModeToggleText => IsStaticLyricsMode ? "Live Synced" : "Static View";
    public string ModeToggleGlyph => IsStaticLyricsMode ? "\uE895" : "\uE8C4";

    public void ToggleStaticLyricsMode()
    {
        IsStaticLyricsMode = !IsStaticLyricsMode;
    }

    public int? PreviousOffsetMs
    {
        get => _previousOffsetMs;
        private set
        {
            if (_previousOffsetMs != value)
            {
                _previousOffsetMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanUndoCalibration));
            }
        }
    }

    public bool CanUndoCalibration => _previousOffsetMs.HasValue;

    public string? CalibrationToastMessage
    {
        get => _calibrationToastMessage;
        set
        {
            if (_calibrationToastMessage != value)
            {
                _calibrationToastMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCalibrationToastVisible
    {
        get => _isCalibrationToastVisible;
        set
        {
            if (_isCalibrationToastVisible != value)
            {
                _isCalibrationToastVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalibrationToastVisibility));
            }
        }
    }

    public Visibility CalibrationToastVisibility =>
        _isCalibrationToastVisible ? Visibility.Visible : Visibility.Collapsed;

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

    public LyricsViewModel(
        SignalRPlaybackClient client,
        ThemeManager? themeManager = null,
        ResponsiveLayoutManager? layoutManager = null)
    {
        _client = client;
        _themeManager = themeManager ?? ThemeManager.Instance;
        _layoutManager = layoutManager ?? ResponsiveLayoutManager.Instance;

        try
        {
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        }
        catch
        {
            _dispatcherQueue = null;
        }

        _layoutManager.LayoutChanged += OnLayoutManagerChanged;
        _layoutManager.BreakpointChanged += OnLayoutBreakpointChanged;
        _themeManager.PaletteChanged += OnPaletteChanged;

        LyricLines.CollectionChanged += OnLyricLinesCollectionChanged;

        _client.ConnectionStateChanged += state => RunOnUIThread(() => ConnectionStatus = state);
        _client.PlaybackStateReceived += state => RunOnUIThread(() => OnPlaybackStateReceived(state));
        _client.LyricsReceived += lyrics => RunOnUIThread(() => OnLyricsReceived(lyrics));
        _client.TrackOffsetReceived += offset => RunOnUIThread(() => OnTrackOffsetReceived(offset));
        _client.SessionsReceived += sessions => RunOnUIThread(() => OnSessionsReceived(sessions));
        _client.DiagnosticsReceived += diag => RunOnUIThread(() => OnDiagnosticsReceived(diag));

        _ticker = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _ticker.Tick += OnTick;
        _ticker.Start();
    }

    private void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue is not null)
        {
            try
            {
                if (!_dispatcherQueue.HasThreadAccess)
                {
                    _dispatcherQueue.TryEnqueue(() => action());
                    return;
                }
            }
            catch
            {
            }
        }
        action();
    }

    private void OnLyricLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            double active = _layoutManager.ActiveLyricsFontSize;
            double inactive = _layoutManager.InactiveLyricsFontSize;
            double past = _layoutManager.PastLyricsFontSize;
            foreach (LyricLineViewModel item in e.NewItems)
            {
                item.RefreshFontSizes(active, inactive, past);
            }
        }
    }

    public async Task InitializeAsync()
    {
        await _client.StartAsync();
    }

    public async Task SetSyncOffsetAsync(int offsetMs)
    {
        if (_lastPlaybackState?.CurrentTrack?.Id is string trackId)
        {
            await _client.SetTrackOffsetAsync(trackId, offsetMs);
        }
    }

    public async Task SwitchUserSubscriptionAsync(string? userId)
    {
        await _client.SubscribeToUserAsync(userId);
    }

    public void ToggleKioskMode()
    {
        IsKioskMode = !IsKioskMode;
    }

    public void SetMobileView(MobileViewMode mode)
    {
        _layoutManager.MobileView = mode;
    }

    public void CycleMobileView()
    {
        _layoutManager.CycleMobileView();
    }

    private void OnLayoutManagerChanged()
    {
        RefreshLyricLineSizes();
        NotifyLayoutProperties();
    }

    private void OnLayoutBreakpointChanged(LayoutBreakpoint breakpoint)
    {
        RefreshLyricLineSizes();
        NotifyLayoutProperties();
    }

    private void OnPaletteChanged(ColorPalette palette)
    {
        NotifyThemeProperties();
    }

    private void NotifyLayoutProperties()
    {
        OnPropertyChanged(nameof(CurrentBreakpoint));
        OnPropertyChanged(nameof(MobileView));
        OnPropertyChanged(nameof(SidePanelWidth));
        OnPropertyChanged(nameof(AlbumArtSize));
        OnPropertyChanged(nameof(ContentPadding));
        OnPropertyChanged(nameof(Layout));
    }

    private void NotifyThemeProperties()
    {
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(SurfaceCardBrush));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(PrimaryAccentBrush));
        OnPropertyChanged(nameof(SecondaryAccentBrush));
        OnPropertyChanged(nameof(TextPrimaryBrush));
        OnPropertyChanged(nameof(TextSecondaryBrush));
        OnPropertyChanged(nameof(TextMutedBrush));
        OnPropertyChanged(nameof(GlowBrush));
        OnPropertyChanged(nameof(ActivePrimaryAccentColor));
        OnPropertyChanged(nameof(ActiveBackgroundColor));
        OnPropertyChanged(nameof(Theme));
    }

    public void RefreshLyricLineSizes()
    {
        double active = _layoutManager.ActiveLyricsFontSize;
        double inactive = _layoutManager.InactiveLyricsFontSize;
        double past = _layoutManager.PastLyricsFontSize;

        foreach (LyricLineViewModel line in LyricLines)
        {
            line.RefreshFontSizes(active, inactive, past);
        }
    }

    private void OnPlaybackStateReceived(PlaybackStatePayload? state)
    {
        if (state is null) return;

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

        TrackInfoPayload track = state.CurrentTrack;
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

        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));

        // Update Theme Manager for Dynamic Palette Sampling
        _themeManager.UpdateTrackMetadata(track.Title, track.Artist, track.AlbumArtUrl);

        long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _client.ClockOffsetMs;
        long targetMs = state.ProgressMs;
        if (state.IsPlaying)
        {
            long serverTimestamp = state.TimestampUtc.ToUnixTimeMilliseconds();
            long elapsed = Math.Max(0, localNow - serverTimestamp);
            targetMs = Math.Max(0, state.ProgressMs + elapsed - DefaultAcousticLatencyCompensationMs);
        }
        else
        {
            targetMs = Math.Max(0, state.ProgressMs - DefaultAcousticLatencyCompensationMs);
        }

        long drift = Math.Abs(_interpolatedProgressMs - targetMs);
        if (drift > 1500 || !_isPlaying)
        {
            _interpolatedProgressMs = targetMs;
            _lastTickTimestampMs = localNow;
        }
    }

    private void OnLyricsReceived(LyricsPayload? lyrics)
    {
        if (lyrics is null) return;

        _lastLyrics = lyrics;
        HasLyrics = lyrics.Lines is not null && lyrics.Lines.Count > 0;
        IsInstrumental = lyrics.IsInstrumental;

        LyricLines.Clear();
        if (lyrics.Lines is not null)
        {
            double active = _layoutManager.ActiveLyricsFontSize;
            double inactive = _layoutManager.InactiveLyricsFontSize;
            double past = _layoutManager.PastLyricsFontSize;

            for (int i = 0; i < lyrics.Lines.Count; i++)
            {
                LyricLinePayload line = lyrics.Lines[i];
                if (line is null) continue;
                LyricLineViewModel lineVm = new()
                {
                    TimestampMs = line.TimestampMs,
                    Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,
                    IsCalibrationMode = _isCalibrationMode
                };

                long nextTimestamp = (i < lyrics.Lines.Count - 1 && lyrics.Lines[i + 1] is not null)
                    ? lyrics.Lines[i + 1].TimestampMs
                    : line.TimestampMs + 4000;
                TimeSpan lineDuration = TimeSpan.FromMilliseconds(Math.Max(1000, nextTimestamp - line.TimestampMs));
                lineVm.PopulateWords(lineDuration);

                lineVm.LineClicked += async l => await CalibrateToTimestampAsync(l.TimestampMs);
                lineVm.WordClicked += async w => await CalibrateToTimestampAsync(w.TimestampMs);

                lineVm.RefreshFontSizes(active, inactive, past);
                LyricLines.Add(lineVm);
            }
        }
        ActiveLineIndex = -1;

        OnPropertyChanged(nameof(StaticLyricsText));
        OnPropertyChanged(nameof(SyncedLyricsVisibility));
        OnPropertyChanged(nameof(StaticLyricsVisibility));
        OnPropertyChanged(nameof(ModeToggleVisibility));
    }

    private void OnTrackOffsetReceived(TrackOffsetPayload? offset)
    {
        if (offset is null) return;

        if (_lastPlaybackState?.CurrentTrack?.Id == offset.TrackId)
        {
            _userOffsetMs = offset.OffsetMs;
            double seconds = _userOffsetMs / 1000.0;
            OffsetText = $"{(_userOffsetMs >= 0 ? "+" : "")}{seconds:0.0}s";
        }
    }

    private void OnSessionsReceived(IReadOnlyList<AuthorizedSessionPayload>? sessions)
    {
        Sessions.Clear();
        if (sessions is null || sessions.Count == 0)
        {
            AuthorizedSessionsCount = 0;
            ActiveUserName = "None";
            ActiveUserId = null;
            OnPropertyChanged(nameof(CurrentUserSession));
            OnPropertyChanged(nameof(NoSessionsVisibility));
            OnPropertyChanged(nameof(HasSessionsVisibility));
            OnPropertyChanged(nameof(IsAuthorized));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ConnectButtonGlyph));
            return;
        }

        foreach (AuthorizedSessionPayload s in sessions)
        {
            if (s is not null)
            {
                Sessions.Add(s);
            }
        }
        AuthorizedSessionsCount = sessions.Count;

        AuthorizedSessionPayload? current = sessions.FirstOrDefault(s => s is not null);
        if (current is not null)
        {
            ActiveUserName = current.DisplayName;
            ActiveUserId = current.Id;
        }

        OnPropertyChanged(nameof(CurrentUserSession));
        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));
    }

    private void OnDiagnosticsReceived(DiagnosticsPayload? diag)
    {
        if (diag is null) return;

        PollerStatus = diag.PollerStatus ?? "Idle";
        ConnectedClients = diag.ConnectedClients;
        AuthorizedSessionsCount = diag.AuthorizedSessions;
        ActivePollIntervalMs = diag.ActivePollIntervalMs;
        if (!string.IsNullOrEmpty(diag.ActiveUserName))
        {
            ActiveUserName = diag.ActiveUserName;
        }
        ActiveUserId = diag.ActiveUserId;

        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));
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

        TrackInfoPayload track = _lastPlaybackState.CurrentTrack;
        long durationMs = track.DurationMs;
        if (durationMs <= 0) durationMs = 1;

        long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _client.ClockOffsetMs;

        if (_lastPlaybackState.IsPlaying)
        {
            long dt = _lastTickTimestampMs > 0 ? Math.Clamp(localNow - _lastTickTimestampMs, 1, 100) : 16;
            _lastTickTimestampMs = localNow;

            long serverTimestamp = _lastPlaybackState.TimestampUtc.ToUnixTimeMilliseconds();
            long elapsed = Math.Max(0, localNow - serverTimestamp);
            long targetMs = Math.Max(0, _lastPlaybackState.ProgressMs + elapsed - DefaultAcousticLatencyCompensationMs);

            // Monotonic Phase-Locked Loop (PLL) tracking
            long error = targetMs - _interpolatedProgressMs;
            if (Math.Abs(error) > 1500)
            {
                _interpolatedProgressMs = targetMs;
            }
            else
            {
                // Slew rate adjustment: steer smoothly up to +/- 5% to lock phase without jumping
                double slewAdjustment = Math.Clamp(error / 500.0, -0.05, 0.05);
                double deltaProgress = dt * (1.0 + slewAdjustment);
                _interpolatedProgressMs += (long)Math.Round(deltaProgress);
            }
        }
        else
        {
            _interpolatedProgressMs = Math.Max(0, _lastPlaybackState.ProgressMs - DefaultAcousticLatencyCompensationMs);
            _lastTickTimestampMs = localNow;
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
        _userOffsetMs = Math.Clamp(_userOffsetMs + deltaMs, -30000, 30000);
        double seconds = _userOffsetMs / 1000.0;
        OffsetText = $"{(_userOffsetMs >= 0 ? "+" : "")}{seconds:0.0}s";
        await _client.SetTrackOffsetAsync(_lastPlaybackState.CurrentTrack.Id, _userOffsetMs);
    }

    public async Task ResetOffsetAsync()
    {
        if (_lastPlaybackState?.CurrentTrack is null) return;
        _userOffsetMs = 0;
        OffsetText = "+0.0s";
        await _client.SetTrackOffsetAsync(_lastPlaybackState.CurrentTrack.Id, 0);
    }

    public void ToggleCalibrationMode()
    {
        IsCalibrationMode = !IsCalibrationMode;
    }

    public async Task CalibrateToTimestampAsync(long targetLyricTimestampMs)
    {
        if (_lastPlaybackState?.CurrentTrack is null) return;
        if (!_isCalibrationMode) return;

        int currentProgress = (int)_interpolatedProgressMs;
        int rawOffset = (int)(targetLyricTimestampMs - currentProgress);

        // Clamp to a sane track calibration window (+/- 20 seconds)
        int newOffset = Math.Clamp(rawOffset, -20000, 20000);
        int delta = newOffset - _userOffsetMs;

        PreviousOffsetMs = _userOffsetMs;
        _userOffsetMs = newOffset;
        double seconds = _userOffsetMs / 1000.0;
        OffsetText = $"{(_userOffsetMs >= 0 ? "+" : "")}{seconds:0.0}s";

        double deltaSec = delta / 1000.0;
        string deltaStr = $"{(delta >= 0 ? "+" : "")}{deltaSec:0.0}s";
        CalibrationToastMessage = $"Offset calibrated: {OffsetText} (Δ {deltaStr})";
        IsCalibrationToastVisible = true;
        StartToastTimer();

        await _client.SetTrackOffsetAsync(_lastPlaybackState.CurrentTrack.Id, newOffset);
    }

    public async Task UndoLastCalibrationAsync()
    {
        if (!_previousOffsetMs.HasValue || _lastPlaybackState?.CurrentTrack is null) return;

        _userOffsetMs = _previousOffsetMs.Value;
        PreviousOffsetMs = null;
        double seconds = _userOffsetMs / 1000.0;
        OffsetText = $"{(_userOffsetMs >= 0 ? "+" : "")}{seconds:0.0}s";
        CalibrationToastMessage = $"Reverted offset to {OffsetText}";
        IsCalibrationToastVisible = true;
        StartToastTimer();

        await _client.SetTrackOffsetAsync(_lastPlaybackState.CurrentTrack.Id, _userOffsetMs);
    }

    public void DismissCalibrationToast()
    {
        IsCalibrationToastVisible = false;
        _toastDismissTimer?.Stop();
    }

    private void StartToastTimer()
    {
        if (_toastDismissTimer is null)
        {
            _toastDismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _toastDismissTimer.Tick += (s, e) =>
            {
                _toastDismissTimer.Stop();
                IsCalibrationToastVisible = false;
            };
        }
        else
        {
            _toastDismissTimer.Stop();
        }
        _toastDismissTimer.Start();
    }

    public async Task SubscribeToUserAsync(string? userId)
    {
        await _client.SubscribeToUserAsync(userId);
    }

    public async Task LogoutAsync()
    {
        await _client.LogoutAsync();
        Sessions.Clear();
        AuthorizedSessionsCount = 0;
        ActiveUserId = null;
        ActiveUserName = "None";
        CurrentTitle = "No Track Playing";
        CurrentArtist = "Play music on Spotify to begin";
        CurrentAlbum = string.Empty;
        AlbumArtUrl = null;
        IsPlaying = false;
        LyricLines.Clear();
        HasLyrics = false;
        ActiveLineIndex = -1;
        _themeManager.UpdateTrackMetadata(null, null, null);
        OnPropertyChanged(nameof(CurrentUserSession));
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));
        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
    }

    private static string FormatTime(long ms)
    {
        TimeSpan ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
