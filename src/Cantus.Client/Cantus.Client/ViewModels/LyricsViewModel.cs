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

    // Compensates for the delay between a position estimate and the audio
    // actually reaching the listener's ears (Bluetooth speakers, TV audio
    // chains). Persisted per device; [ and ] adjust it in 50ms steps.
    private const string SETTINGS_KEY_LATENCY = "cantus_latency_ms";
    private const int DEFAULT_LATENCY_COMPENSATION_MS = 200;
    private const int LATENCY_STEP_MS = 50;
    private const int MIN_LATENCY_COMPENSATION_MS = 0;
    private const int MAX_LATENCY_COMPENSATION_MS = 1000;
    private int _latencyCompensationMs = LoadLatencyCompensation();

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

    private const int TRANSPORT_STATUS_DURATION_MS = 8000;
    private readonly DispatcherTimer _transportStatusTimer;
    private string _transportStatusText = string.Empty;

    private double _progressFraction;
    private string _progressText = "00:00";
    private string _totalDurationText = "00:00";
    private string _offsetText = "+0.0s";
    private bool _hasLyrics;
    private bool _isInstrumental;
    private bool _isInstrumentalBreak;
    private string _instrumentalBreakText = string.Empty;
    private bool _isKioskMode;
    private bool _isStaticLyricsMode;
    private const string SETTINGS_KEY_AUTOSCROLL = "cantus_autoscroll";
    private bool _isAutoScrollEnabled = LoadAutoScrollPreference();
    private bool _isUserScrollingPaused;
    private Microsoft.UI.Xaml.Media.ImageSource? _ambientBackgroundSource;
    private string? _lastAmbientArtworkUrl;
    private const string SETTINGS_KEY_KARAOKE = "cantus_karaoke";
    private bool _isKaraokeModeEnabled = LoadKaraokePreference();

    public ObservableCollection<LyricLineViewModel> LyricLines { get; } = new();
    public ObservableCollection<AuthorizedSessionPayload> Sessions { get; } = new();
    public ThemeManager Theme => _themeManager;
    public ResponsiveLayoutManager Layout => _layoutManager;
    public string ServerBaseUrl => _client.ServerBaseUrl;

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
    public Microsoft.UI.Xaml.Media.ImageSource? AmbientBackgroundSource => _ambientBackgroundSource;
    public Visibility AmbientBackgroundVisibility => _ambientBackgroundSource is null ? Visibility.Collapsed : Visibility.Visible;

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

    public string AppVersion => BuildInfo.Version;

    public string CommitSha => BuildInfo.CommitSha;

    public string AppVersionDisplay => $"Cantus v{BuildInfo.Version}";

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

    public string TransportStatusText => _transportStatusText;

    public Visibility TransportStatusVisibility =>
        string.IsNullOrEmpty(_transportStatusText) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Sends pause when playing and resume when paused. On success the playing
    /// flag flips optimistically; the command also triggers server polls, so
    /// the authoritative state arrives within a second.
    /// </summary>
    public async Task TogglePlayPauseAsync()
    {
        if (_lastPlaybackState?.CurrentTrack is null)
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
        if (_lastPlaybackState?.CurrentTrack is null)
        {
            return;
        }

        ReportTransportResult(await _client.SendPlayerCommandAsync("next"));
    }

    public async Task SkipToPreviousAsync()
    {
        if (_lastPlaybackState?.CurrentTrack is null)
        {
            return;
        }

        ReportTransportResult(await _client.SendPlayerCommandAsync("previous"));
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
            PlayerCommandResult.MissingPermissions =>
                "Spotify rejected the command. Log out and reconnect your account to grant playback control (Premium required).",
            PlayerCommandResult.NoActiveDevice =>
                "No active Spotify device. Start playback in any Spotify app first.",
            PlayerCommandResult.Failed => "Couldn't reach Spotify. Try again.",
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
        set
        {
            if (_hasLyrics != value)
            {
                _hasLyrics = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SyncedLyricsVisibility));
                OnPropertyChanged(nameof(StaticLyricsVisibility));
                OnPropertyChanged(nameof(ModeToggleVisibility));
                OnPropertyChanged(nameof(AutoScrollToggleVisibility));
                OnPropertyChanged(nameof(KaraokeToggleVisibility));
                OnPropertyChanged(nameof(ResumeAutoScrollVisibility));
            }
        }
    }

    public bool HasSyncedLyrics => _lastLyrics?.Lines is not null && _lastLyrics.Lines.Count > 0;
    public bool HasPlainLyrics => !string.IsNullOrWhiteSpace(_lastLyrics?.PlainLyrics);

    // The empty lyrics stage previously always said "Connect Spotify and play
    // music" - misleading when a track is already playing and lyrics simply
    // don't exist for it.
    public string EmptyStateTitle =>
        _lastPlaybackState?.CurrentTrack is not null ? "No Lyrics Found" : "Waiting for Lyrics...";

    public string EmptyStateSubtitle =>
        _lastPlaybackState?.CurrentTrack is not null
            ? "Lyrics for this track aren't available yet."
            : "Connect Spotify and play music to see lyrics.";

    private void NotifyEmptyStateText()
    {
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateSubtitle));
    }

    public bool IsInstrumental
    {
        get => _isInstrumental;
        set { if (_isInstrumental != value) { _isInstrumental = value; OnPropertyChanged(); } }
    }

    public bool IsInstrumentalBreak
    {
        get => _isInstrumentalBreak;
        set
        {
            if (_isInstrumentalBreak != value)
            {
                _isInstrumentalBreak = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InstrumentalBreakVisibility));
            }
        }
    }

    public string InstrumentalBreakText
    {
        get => _instrumentalBreakText;
        set { if (_instrumentalBreakText != value) { _instrumentalBreakText = value; OnPropertyChanged(); } }
    }

    public Visibility InstrumentalBreakVisibility =>
        IsInstrumentalBreak && HasSyncedLyrics && !IsStaticLyricsMode ? Visibility.Visible : Visibility.Collapsed;

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
                OnPropertyChanged(nameof(AutoScrollToggleVisibility));
                OnPropertyChanged(nameof(InstrumentalBreakVisibility));
                OnPropertyChanged(nameof(KaraokeToggleVisibility));
                OnPropertyChanged(nameof(ResumeAutoScrollVisibility));
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

    public Visibility ModeToggleVisibility => (HasSyncedLyrics && HasLyrics) ? Visibility.Visible : Visibility.Collapsed;
    public string ModeToggleText => IsStaticLyricsMode ? "Live Synced" : "Static View";
    public string ModeToggleGlyph => IsStaticLyricsMode ? "\uE895" : "\uE8C4";

    public bool IsAutoScrollEnabled
    {
        get => _isAutoScrollEnabled;
        set
        {
            if (_isAutoScrollEnabled != value)
            {
                _isAutoScrollEnabled = value;
                SaveAutoScrollPreference(value);
                if (!value)
                {
                    IsUserScrollingPaused = false;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoScrollToggleText));
                OnPropertyChanged(nameof(AutoScrollToggleGlyph));
                OnPropertyChanged(nameof(AutoScrollToggleVisibility));
                OnPropertyChanged(nameof(ResumeAutoScrollVisibility));
                AutoScrollEnabledChanged?.Invoke(value);
            }
        }
    }

    public bool IsUserScrollingPaused
    {
        get => _isUserScrollingPaused;
        private set
        {
            if (_isUserScrollingPaused != value)
            {
                _isUserScrollingPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ResumeAutoScrollVisibility));
            }
        }
    }

    public int LatencyCompensationMs
    {
        get => _latencyCompensationMs;
        internal set
        {
            int clamped = Math.Clamp(value, MIN_LATENCY_COMPENSATION_MS, MAX_LATENCY_COMPENSATION_MS);
            if (_latencyCompensationMs != clamped)
            {
                _latencyCompensationMs = clamped;
                SaveLatencyCompensation(clamped);
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalibrationText));
            }
        }
    }

    public string CalibrationText => $"{_latencyCompensationMs} ms";

    /// <summary>
    /// Adjusts the device latency compensation. Positive deltas render lyrics
    /// later; negative deltas render them earlier (the fix when lyrics trail
    /// the audio). Bound to the [ and ] keys and the settings steppers.
    /// </summary>
    public void AdjustLatencyCompensation(int deltaMs)
    {
        LatencyCompensationMs = _latencyCompensationMs + deltaMs;
    }

    internal long InterpolatedProgressMs => _interpolatedProgressMs;

    public string AutoScrollToggleText => IsAutoScrollEnabled ? "Autoscroll" : "Free Scroll";
    public string AutoScrollToggleGlyph => IsAutoScrollEnabled ? "\uE73E" : "\uE711";
    public Visibility AutoScrollToggleVisibility => (HasSyncedLyrics && HasLyrics && !IsStaticLyricsMode) ? Visibility.Visible : Visibility.Collapsed;

    public bool IsKaraokeModeEnabled
    {
        get => _isKaraokeModeEnabled;
        set
        {
            if (_isKaraokeModeEnabled != value)
            {
                _isKaraokeModeEnabled = value;
                SaveKaraokePreference(value);
                foreach (LyricLineViewModel line in LyricLines)
                {
                    line.SetKaraokeEnabled(value);
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(KaraokeToggleVisibility));
            }
        }
    }

    public Visibility KaraokeToggleVisibility => (HasSyncedLyrics && HasLyrics && !IsStaticLyricsMode) ? Visibility.Visible : Visibility.Collapsed;

    public void ToggleKaraokeMode()
    {
        IsKaraokeModeEnabled = !IsKaraokeModeEnabled;
    }
    public Visibility ResumeAutoScrollVisibility => (IsAutoScrollEnabled && IsUserScrollingPaused && HasSyncedLyrics && !IsStaticLyricsMode) ? Visibility.Visible : Visibility.Collapsed;

    public event Action<bool>? AutoScrollEnabledChanged;
    public event Action? AutoScrollResumed;

    public void ToggleStaticLyricsMode()
    {
        IsStaticLyricsMode = !IsStaticLyricsMode;
    }

    public void ToggleAutoScroll()
    {
        IsAutoScrollEnabled = !IsAutoScrollEnabled;
    }

    public void SetUserScrollingPaused(bool paused)
    {
        if (!IsAutoScrollEnabled && paused) return;
        IsUserScrollingPaused = paused;
    }

    public void ResumeAutoScroll()
    {
        IsUserScrollingPaused = false;
        AutoScrollResumed?.Invoke();
        if (ActiveLineIndex >= 0)
        {
            ActiveLineChanged?.Invoke(ActiveLineIndex);
        }
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

    /// <summary>Raised after a new lyrics payload replaces the line collection, so views can reset their scroll position.</summary>
    public event Action? LyricsReloaded;

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
        _client.AuthSessionReceived += session => RunOnUIThread(() => OnAuthSessionReceived(session));
        _client.SessionRevoked += userId => RunOnUIThread(() => OnSessionRevoked(userId));
        _client.DiagnosticsReceived += diag => RunOnUIThread(() => OnDiagnosticsReceived(diag));

        _ticker = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _ticker.Tick += OnTick;
        _ticker.Start();

        _transportStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TRANSPORT_STATUS_DURATION_MS)
        };
        _transportStatusTimer.Tick += (s, e) => HideTransportStatus();
    }

    public string ClientId => _client.ClientId;

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

    public async Task ReportVisibilityAsync(bool isVisible)
    {
        await _client.ReportVisibilityAsync(isVisible);
    }

    public async Task RefreshPlaybackAsync()
    {
        await _client.RefreshPlaybackAsync();
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
        UpdateAmbientBackground();
        NotifyThemeProperties();
    }

    private void UpdateAmbientBackground()
    {
        string? artworkUrl = _themeManager.AmbientArtworkUrl;
        if (artworkUrl == _lastAmbientArtworkUrl)
        {
            return;
        }

        _lastAmbientArtworkUrl = artworkUrl;

        if (artworkUrl is null)
        {
            _ambientBackgroundSource = null;
        }
        else
        {
            try
            {
                _ambientBackgroundSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(artworkUrl));
            }
            catch (Exception)
            {
                // Headless test environments cannot create XAML bitmaps.
                _ambientBackgroundSource = null;
            }
        }

        OnPropertyChanged(nameof(AmbientBackgroundSource));
        OnPropertyChanged(nameof(AmbientBackgroundVisibility));
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
            NotifyEmptyStateText();
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
        NotifyEmptyStateText();
        if (!string.IsNullOrEmpty(state.ActiveUserDisplayName))
        {
            ActiveUserName = state.ActiveUserDisplayName;
        }
        ActiveUserId = state.ActiveUserId;

        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));

        _themeManager.UpdateTrackMetadata(track.Title, track.Artist, track.AlbumArtUrl);

        long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _client.ClockOffsetMs;
        long targetMs = state.ProgressMs;
        if (state.IsPlaying)
        {
            long serverTimestamp = state.TimestampUtc.ToUnixTimeMilliseconds();
            long elapsed = Math.Max(0, localNow - serverTimestamp);
            targetMs = Math.Max(0, state.ProgressMs + elapsed - _latencyCompensationMs);
        }
        else
        {
            targetMs = Math.Max(0, state.ProgressMs - _latencyCompensationMs);
        }

        long drift = Math.Abs(_interpolatedProgressMs - targetMs);
        if (drift > 1500 || !_isPlaying)
        {
            _interpolatedProgressMs = targetMs;
            _lastTickTimestampMs = localNow;
        }
    }

    // Mirrors EvaluateInstrumentalBreak's definition of a break-length gap: a
    // line whose gap to the next line is at least this long has an
    // instrumental tail, and the underline should not pretend the whole gap is
    // sung.
    private const long INSTRUMENTAL_TAIL_GAP_MS = 8000;
    private const long SUNG_MS_PER_CHAR = 90;
    private const long SUNG_BASE_MS = 600;
    private const long MIN_ESTIMATED_SUNG_MS = 1500;

    /// <summary>
    /// How long the active line's underline takes to fill. For continuous
    /// singing the gap to the next line IS the sung duration, and using it
    /// exactly makes the bar complete just as the next line activates. But
    /// when the gap is break-length (a long musical passage after the words),
    /// filling over the whole gap misrepresents the singing badly, so the fill
    /// is capped at a text-length estimate and the bar then sits complete
    /// while the instrumental-break indicator covers the remainder. "♪"
    /// placeholder lines keep the full gap: there the fill genuinely tracks
    /// progress through the interlude.
    /// </summary>
    internal static long ComputeLineFillDurationMs(string displayText, long gapMs)
    {
        if (gapMs < INSTRUMENTAL_TAIL_GAP_MS || displayText == "♪")
        {
            return gapMs;
        }

        long estimate = SUNG_BASE_MS + (displayText.Length * SUNG_MS_PER_CHAR);
        return Math.Clamp(estimate, MIN_ESTIMATED_SUNG_MS, gapMs);
    }

    private void OnLyricsReceived(LyricsPayload? lyrics)
    {
        if (lyrics is null) return;

        _lastLyrics = lyrics;
        bool hasSynced = lyrics.Lines is not null && lyrics.Lines.Count > 0;
        bool hasPlain = !string.IsNullOrWhiteSpace(lyrics.PlainLyrics);
        HasLyrics = hasSynced || hasPlain;
        IsInstrumental = lyrics.IsInstrumental;

        if (hasSynced)
        {
            IsStaticLyricsMode = false;
        }
        else if (hasPlain)
        {
            IsStaticLyricsMode = true;
        }
        else
        {
            IsStaticLyricsMode = false;
        }

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

                long? nextLineTimestampMs = null;
                for (int j = i + 1; j < lyrics.Lines.Count; j++)
                {
                    if (lyrics.Lines[j] is not null)
                    {
                        nextLineTimestampMs = lyrics.Lines[j].TimestampMs;
                        break;
                    }
                }

                string displayText = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;
                long? lineDurationMs = nextLineTimestampMs.HasValue && nextLineTimestampMs.Value > line.TimestampMs
                    ? ComputeLineFillDurationMs(displayText, nextLineTimestampMs.Value - line.TimestampMs)
                    : null;

                LyricLineViewModel lineVm = new()
                {
                    TimestampMs = line.TimestampMs,
                    Text = displayText,
                    DurationMs = lineDurationMs
                };

                lineVm.RefreshFontSizes(active, inactive, past);
                lineVm.SetKaraokeEnabled(_isKaraokeModeEnabled);
                LyricLines.Add(lineVm);
            }
        }
        ActiveLineIndex = -1;
        IsUserScrollingPaused = false;

        OnPropertyChanged(nameof(StaticLyricsText));
        OnPropertyChanged(nameof(SyncedLyricsVisibility));
        OnPropertyChanged(nameof(StaticLyricsVisibility));
        OnPropertyChanged(nameof(ModeToggleVisibility));
        OnPropertyChanged(nameof(AutoScrollToggleVisibility));
        OnPropertyChanged(nameof(InstrumentalBreakVisibility));
        OnPropertyChanged(nameof(KaraokeToggleVisibility));
        OnPropertyChanged(nameof(ResumeAutoScrollVisibility));
        OnPropertyChanged(nameof(HasSyncedLyrics));
        OnPropertyChanged(nameof(HasPlainLyrics));

        // ActiveLineIndex resets to -1 here, and nothing scrolls on a negative
        // index - so without this event the stage keeps the previous track's
        // scroll offset (often the bottom) until the first line activates.
        LyricsReloaded?.Invoke();
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
            CurrentTitle = "No Track Playing";
            CurrentArtist = "Play music on Spotify to begin";
            CurrentAlbum = string.Empty;
            AlbumArtUrl = null;
            IsPlaying = false;
            _lastLyrics = null;
            LyricLines.Clear();
            HasLyrics = false;
            IsStaticLyricsMode = false;
            ActiveLineIndex = -1;
            _themeManager.UpdateTrackMetadata(null, null, null);
            OnPropertyChanged(nameof(CurrentUserSession));
            OnPropertyChanged(nameof(NoSessionsVisibility));
            OnPropertyChanged(nameof(HasSessionsVisibility));
            OnPropertyChanged(nameof(IsAuthorized));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ConnectButtonGlyph));
            OnPropertyChanged(nameof(HasSyncedLyrics));
            OnPropertyChanged(nameof(HasPlainLyrics));
            OnPropertyChanged(nameof(ModeToggleVisibility));
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
            _ = _client.SubscribeToUserAsync(current.Id);
        }

        OnPropertyChanged(nameof(CurrentUserSession));
        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));
    }

    private void OnAuthSessionReceived(AuthorizedSessionPayload? session)
    {
        if (session is null) return;
        Sessions.Clear();
        Sessions.Add(session);
        AuthorizedSessionsCount = 1;
        ActiveUserName = session.DisplayName;
        ActiveUserId = session.Id;
        _ = _client.SubscribeToUserAsync(session.Id);
        OnPropertyChanged(nameof(CurrentUserSession));
        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));
    }

    private void OnSessionRevoked(string? userId)
    {
        if (string.IsNullOrEmpty(userId) || ActiveUserId == userId)
        {
            Sessions.Clear();
            AuthorizedSessionsCount = 0;
            ActiveUserName = "None";
            ActiveUserId = null;
            CurrentTitle = "No Track Playing";
            CurrentArtist = "Play music on Spotify to begin";
            CurrentAlbum = string.Empty;
            AlbumArtUrl = null;
            IsPlaying = false;
            _lastLyrics = null;
            LyricLines.Clear();
            HasLyrics = false;
            IsStaticLyricsMode = false;
            ActiveLineIndex = -1;
            _themeManager.UpdateTrackMetadata(null, null, null);
            OnPropertyChanged(nameof(CurrentUserSession));
            OnPropertyChanged(nameof(NoSessionsVisibility));
            OnPropertyChanged(nameof(HasSessionsVisibility));
            OnPropertyChanged(nameof(IsAuthorized));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ConnectButtonGlyph));
            OnPropertyChanged(nameof(HasSyncedLyrics));
            OnPropertyChanged(nameof(HasPlainLyrics));
            OnPropertyChanged(nameof(ModeToggleVisibility));
        }
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
            long targetMs = Math.Max(0, _lastPlaybackState.ProgressMs + elapsed - _latencyCompensationMs);

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
            _interpolatedProgressMs = Math.Max(0, _lastPlaybackState.ProgressMs - _latencyCompensationMs);
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

            if (_isKaraokeModeEnabled && idx >= 0 && idx < LyricLines.Count)
            {
                LyricLines[idx].UpdateLineProgress(currentWithOffset);
            }

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
        _lastPlaybackState = null;
        _lastLyrics = null;
        LyricLines.Clear();
        HasLyrics = false;
        IsStaticLyricsMode = false;
        ActiveLineIndex = -1;
        _themeManager.UpdateTrackMetadata(null, null, null);
        NotifyEmptyStateText();
        OnPropertyChanged(nameof(CurrentUserSession));
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));
        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        OnPropertyChanged(nameof(HasSyncedLyrics));
        OnPropertyChanged(nameof(HasPlainLyrics));
        OnPropertyChanged(nameof(ModeToggleVisibility));
    }

    private static string FormatTime(long ms)
    {
        TimeSpan ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    private static bool LoadAutoScrollPreference()
    {
        try
        {
            object? val = Windows.Storage.ApplicationData.Current?.LocalSettings?.Values[SETTINGS_KEY_AUTOSCROLL];
            if (val is bool b) return b;
            if (val is string s && bool.TryParse(s, out bool parsed)) return parsed;
        }
        catch
        {
        }
        return true;
    }

    private static void SaveAutoScrollPreference(bool value)
    {
        try
        {
            if (Windows.Storage.ApplicationData.Current?.LocalSettings?.Values is { } settings)
            {
                settings[SETTINGS_KEY_AUTOSCROLL] = value;
            }
        }
        catch
        {
        }
    }

    private static int LoadLatencyCompensation()
    {
        try
        {
            object? val = Windows.Storage.ApplicationData.Current?.LocalSettings?.Values[SETTINGS_KEY_LATENCY];
            if (val is int i) return Math.Clamp(i, MIN_LATENCY_COMPENSATION_MS, MAX_LATENCY_COMPENSATION_MS);
            if (val is string s && int.TryParse(s, out int parsed))
            {
                return Math.Clamp(parsed, MIN_LATENCY_COMPENSATION_MS, MAX_LATENCY_COMPENSATION_MS);
            }
        }
        catch
        {
        }
        return DEFAULT_LATENCY_COMPENSATION_MS;
    }

    private static void SaveLatencyCompensation(int value)
    {
        try
        {
            if (Windows.Storage.ApplicationData.Current?.LocalSettings?.Values is { } settings)
            {
                settings[SETTINGS_KEY_LATENCY] = value;
            }
        }
        catch
        {
        }
    }

    private static bool LoadKaraokePreference()
    {
        try
        {
            object? val = Windows.Storage.ApplicationData.Current?.LocalSettings?.Values[SETTINGS_KEY_KARAOKE];
            if (val is bool b) return b;
            if (val is string s && bool.TryParse(s, out bool parsed)) return parsed;
        }
        catch
        {
        }
        return true;
    }

    private static void SaveKaraokePreference(bool value)
    {
        try
        {
            if (Windows.Storage.ApplicationData.Current?.LocalSettings?.Values is { } settings)
            {
                settings[SETTINGS_KEY_KARAOKE] = value;
            }
        }
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
