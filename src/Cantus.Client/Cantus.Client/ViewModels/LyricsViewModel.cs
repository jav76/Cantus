using System;
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
    private readonly ISignalRPlaybackClient _client;
    private readonly DispatcherTimer _ticker;
    private readonly ThemeManager _themeManager;
    private readonly ResponsiveLayoutManager _layoutManager;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private readonly ILyricsPreferences _preferences;
    private readonly PlaybackTransport _transport;
    private readonly PlaybackTiming _timing;
    private readonly SessionViewModel _session;

    private LyricsPayload? _lastLyrics;
    private int _activeLineIndex = -1;
    private long _interpolatedProgressMs;
    private long _lastTickTimestampMs;

    private string _connectionStatus = "Connecting...";
    private long _rttMs;
    private long _clockSkewMs;
    private string _pollerStatus = "Idle";
    private int _connectedClients = 1;
    private int _activePollIntervalMs = 1500;
    private string _transportType = "WebSockets";

    private string _currentTitle = "No Track Playing";
    private string _currentArtist = "Play music on Spotify to begin";
    private string _currentAlbum = string.Empty;
    private string? _albumArtUrl;
    private string _deviceName = "Spotify";

    private double _progressFraction;
    private string _progressText = "00:00";
    private string _totalDurationText = "00:00";
    private bool _hasLyrics;
    private bool _isInstrumental;
    private bool _isInstrumentalBreak;
    private string _instrumentalBreakText = string.Empty;
    private bool _isKioskMode;
    private bool _isStaticLyricsMode;
    private bool _isAutoScrollEnabled;
    private bool _isUserScrollingPaused;
    private Microsoft.UI.Xaml.Media.ImageSource? _ambientBackgroundSource;
    private string? _lastAmbientArtworkUrl;
    private const int THEME_TOAST_DURATION_MS = 2000;
    private readonly DispatcherTimer _themeToastTimer;
    private string _themeToastText = string.Empty;
    private bool _isThemeToastVisible;
    private bool _isKaraokeModeEnabled;

    public ObservableCollection<LyricLineViewModel> LyricLines { get; } = new();
    public ObservableCollection<AuthorizedSessionPayload> Sessions => _session.Sessions;
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
    public string ThemeToastText => _themeToastText;

    // On Small the toast overlays the page top (the lyrics stage may be hidden behind
    // another mobile tab); on wider layouts it renders inside the lyrics stage's top
    // row so it sits in the empty space above the lyrics, centered with them.
    public Visibility ThemeToastPageVisibility =>
        _isThemeToastVisible && _layoutManager.CurrentBreakpoint == LayoutBreakpoint.Small
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility ThemeToastStageVisibility =>
        _isThemeToastVisible && _layoutManager.CurrentBreakpoint != LayoutBreakpoint.Small
            ? Visibility.Visible
            : Visibility.Collapsed;

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

    public SessionViewModel Session => _session;

    public string ActiveUserName
    {
        get => _session.ActiveUserName;
        set => _session.ActiveUserName = value;
    }

    public string? ActiveUserId
    {
        get => _session.ActiveUserId;
        set => _session.ActiveUserId = value;
    }

    public int ConnectedClients
    {
        get => _connectedClients;
        set { if (_connectedClients != value) { _connectedClients = value; OnPropertyChanged(); } }
    }

    public int AuthorizedSessionsCount
    {
        get => _session.AuthorizedSessionsCount;
        set => _session.AuthorizedSessionsCount = value;
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

    public PlaybackTransport Transport => _transport;

    public bool IsPlaying
    {
        get => _transport.IsPlaying;
        set => _transport.IsPlaying = value;
    }

    public string PlayPauseGlyph => _transport.PlayPauseGlyph;

    public string TransportStatusText => _transport.TransportStatusText;

    public Visibility TransportStatusVisibility => _transport.TransportStatusVisibility;

    public Task TogglePlayPauseAsync() => _transport.TogglePlayPauseAsync();

    public Task SkipToNextAsync() => _transport.SkipToNextAsync();

    public Task SkipToPreviousAsync() => _transport.SkipToPreviousAsync();

    internal void ReportTransportResult(PlayerCommandResult result) => _transport.ReportTransportResult(result);

    public bool IsShuffled
    {
        get => _transport.IsShuffled;
        set => _transport.IsShuffled = value;
    }

    /// <summary>Spotify repeat state: "off", "track", or "context".</summary>
    public string RepeatMode
    {
        get => _transport.RepeatMode;
        set => _transport.RepeatMode = value;
    }

    public string RepeatGlyph => _transport.RepeatGlyph;

    public Microsoft.UI.Xaml.Media.SolidColorBrush ShuffleButtonForeground => _transport.ShuffleButtonForeground;

    public Microsoft.UI.Xaml.Media.SolidColorBrush RepeatButtonForeground => _transport.RepeatButtonForeground;

    public double VolumeSliderValue => _transport.VolumeSliderValue;

    public string VolumeText => _transport.VolumeText;

    public int? VolumePercent
    {
        get => _transport.VolumePercent;
        set => _transport.VolumePercent = value;
    }

    public Task ToggleShuffleAsync() => _transport.ToggleShuffleAsync();

    public Task CycleRepeatAsync() => _transport.CycleRepeatAsync();

    public Task SeekToFractionAsync(double fraction) => _transport.SeekToFractionAsync(fraction);

    public void RequestVolumeChange(int volumePercent) => _transport.RequestVolumeChange(volumePercent);

    /// <summary>
    /// Seeks playback to the moment a synced line is sung. The tapped line
    /// becomes active and the stage re-centers on it before Spotify answers;
    /// a rejected seek reverts to the last real playback state. No-op for
    /// static or unsynced lyrics, which carry no timestamps.
    /// </summary>
    public async Task SeekToLineAsync(int lineIndex)
    {
        if (!CanSeekToLines || lineIndex < 0 || lineIndex >= LyricLines.Count)
        {
            return;
        }

        // A tap is an explicit "take me there": it ends any manual-scroll pause.
        IsUserScrollingPaused = false;
        await _transport.SeekToLineAsync(LyricLines[lineIndex].TimestampMs, _timing.TrackOffsetMs);
    }

    private void OnTransportSeekApplied(long localNow)
    {
        ResyncInterpolation(localNow);
        int previousIndex = ActiveLineIndex;
        AdvancePlayback();
        if (ActiveLineIndex == previousIndex && ActiveLineIndex >= 0)
        {
            // Tapping the active line restarts it; the stage still re-centers.
            ActiveLineChanged?.Invoke(ActiveLineIndex);
        }
    }

    private void OnTransportSeekReverted(long localNow)
    {
        ResyncInterpolation(localNow);
        AdvancePlayback();
    }

    private void ResyncInterpolation(long localNow)
    {
        if (_transport.PlaybackState is PlaybackStatePayload state)
        {
            _interpolatedProgressMs = ComputeTargetProgressMs(state, localNow);
            _lastTickTimestampMs = localNow;
        }
    }

    private long ComputeTargetProgressMs(PlaybackStatePayload state, long localNow)
        => _timing.ComputeClockTargetMs(OptimisticSeekAnchor.ProjectProgressMs(state, localNow));

    private long GetServerNowMs() => _transport.GetServerNowMs();

    public string DeviceName
    {
        get => _deviceName;
        set { if (_deviceName != value) { _deviceName = value; OnPropertyChanged(); } }
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

    public PlaybackTiming Timing => _timing;

    public string OffsetText => _timing.OffsetText;

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

    /// <summary>
    /// Whether tapping a lyric line seeks: only timestamped lines shown live,
    /// with a track to seek within. Read by the stage's hover/press handlers.
    /// </summary>
    public bool CanSeekToLines =>
        _transport.PlaybackState?.CurrentTrack is not null && HasSyncedLyrics && !IsStaticLyricsMode;

    // The empty lyrics stage previously always said "Connect Spotify and play
    // music" - misleading when a track is already playing and lyrics simply
    // don't exist for it.
    public string EmptyStateTitle =>
        _transport.PlaybackState?.CurrentTrack is not null ? "No Lyrics Found" : "Waiting for Lyrics...";

    public string EmptyStateSubtitle =>
        _transport.PlaybackState?.CurrentTrack is not null
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

    public bool IsAuthorized => _session.IsAuthorized;

    public AuthorizedSessionPayload? CurrentUserSession => _session.CurrentUserSession;

    public string ConnectButtonText => _session.ConnectButtonText;

    public string ConnectButtonGlyph => _session.ConnectButtonGlyph;

    public Visibility NoSessionsVisibility => _session.NoSessionsVisibility;

    public Visibility HasSessionsVisibility => _session.HasSessionsVisibility;

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
                _preferences.IsAutoScrollEnabled = value;
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
        get => _timing.LatencyCompensationMs;
        internal set => _timing.LatencyCompensationMs = value;
    }

    public string CalibrationText => _timing.CalibrationText;

    /// <inheritdoc cref="PlaybackTiming.AdjustLatencyCompensation"/>
    public void AdjustLatencyCompensation(int deltaMs) => _timing.AdjustLatencyCompensation(deltaMs);

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
                _preferences.IsKaraokeModeEnabled = value;
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
        ISignalRPlaybackClient client,
        ThemeManager? themeManager = null,
        ResponsiveLayoutManager? layoutManager = null,
        ILyricsPreferences? preferences = null)
    {
        _client = client;
        _themeManager = themeManager ?? ThemeManager.Instance;
        _layoutManager = layoutManager ?? ResponsiveLayoutManager.Instance;
        _preferences = preferences ?? new LyricsPreferences(new LocalSettingsStore());
        _isAutoScrollEnabled = _preferences.IsAutoScrollEnabled;
        _isKaraokeModeEnabled = _preferences.IsKaraokeModeEnabled;

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
        _themeManager.PropertyChanged += OnThemeManagerPropertyChanged;

        // Built after the palette subscription above so its brush notifications
        // keep following the root's theme notifications.
        _transport = new PlaybackTransport(client, _themeManager);
        _transport.PropertyChanged += OnForwardedPropertyChanged;
        _transport.SeekApplied += OnTransportSeekApplied;
        _transport.SeekReverted += OnTransportSeekReverted;

        _timing = new PlaybackTiming(client, _preferences, () => _transport.PlaybackState?.CurrentTrack?.Id);
        _timing.PropertyChanged += OnForwardedPropertyChanged;

        _session = new SessionViewModel(client);
        _session.PropertyChanged += OnForwardedPropertyChanged;
        _session.SessionEnded += () => ClearPlayback(resetTransport: false);
        _session.LoggedOut += () => ClearPlayback(resetTransport: true);

        LyricLines.CollectionChanged += OnLyricLinesCollectionChanged;

        _client.ConnectionStateChanged += state => RunOnUIThread(() => ConnectionStatus = state);
        _client.PlaybackStateReceived += state => RunOnUIThread(() => OnPlaybackStateReceived(state));
        _client.LyricsReceived += lyrics => RunOnUIThread(() => OnLyricsReceived(lyrics));
        _client.TrackOffsetReceived += offset => RunOnUIThread(() => _timing.ApplyTrackOffset(offset));
        _client.SessionsReceived += sessions => RunOnUIThread(() => _session.ApplySessions(sessions));
        _client.AuthSessionReceived += session => RunOnUIThread(() => _session.ApplyAuthSession(session));
        _client.SessionRevoked += userId => RunOnUIThread(() => _session.ApplySessionRevoked(userId));
        _client.DiagnosticsReceived += diag => RunOnUIThread(() => OnDiagnosticsReceived(diag));

        _ticker = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _ticker.Tick += OnTick;
        _ticker.Start();

        _themeToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(THEME_TOAST_DURATION_MS)
        };
        _themeToastTimer.Tick += (s, e) => HideThemeToast();
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

    public Task SetSyncOffsetAsync(int offsetMs) => _timing.SetSyncOffsetAsync(offsetMs);

    public Task SwitchUserSubscriptionAsync(string? userId) => _session.SubscribeToUserAsync(userId);

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

    private void OnThemeManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeManager.CurrentMode))
        {
            ShowThemeToast(_themeManager.CurrentMode);
        }
    }

    private void ShowThemeToast(ThemeMode mode)
    {
        _themeToastText = mode.GetDisplayName();
        _isThemeToastVisible = true;
        OnPropertyChanged(nameof(ThemeToastText));
        NotifyThemeToastVisibilities();

        _themeToastTimer.Stop();
        _themeToastTimer.Start();
    }

    internal void HideThemeToast()
    {
        _themeToastTimer.Stop();
        if (_isThemeToastVisible)
        {
            _isThemeToastVisible = false;
            NotifyThemeToastVisibilities();
        }
    }

    private void NotifyThemeToastVisibilities()
    {
        OnPropertyChanged(nameof(ThemeToastPageVisibility));
        OnPropertyChanged(nameof(ThemeToastStageVisibility));
    }

    private void NotifyLayoutProperties()
    {
        NotifyThemeToastVisibilities();
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

    // Pass-through members share the transport's, timing's and session's property
    // names, so their notifications are re-raised as-is for views bound to this view model.
    private void OnForwardedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
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

        long localNow = GetServerNowMs();
        state = _transport.ApplyServerState(state, localNow);
        RttMs = _client.RttMs;
        ClockSkewMs = _client.ClockOffsetMs;
        TransportType = _client.TransportType;

        if (state.CurrentTrack is null)
        {
            CurrentTitle = "No Track Playing";
            CurrentArtist = "Play music on Spotify to begin";
            CurrentAlbum = string.Empty;
            AlbumArtUrl = null;
            DeviceName = "No Device";
            _themeManager.UpdateTrackMetadata(null, null, null);
            NotifyEmptyStateText();
            return;
        }

        TrackInfoPayload track = state.CurrentTrack;
        CurrentTitle = track.Title;
        CurrentArtist = track.Artist;
        CurrentAlbum = track.Album ?? string.Empty;
        AlbumArtUrl = track.AlbumArtUrl;
        DeviceName = state.DeviceName ?? "Spotify";
        NotifyEmptyStateText();
        _session.ApplyActiveUser(state.ActiveUserDisplayName, state.ActiveUserId);

        _themeManager.UpdateTrackMetadata(track.Title, track.Artist, track.AlbumArtUrl);

        long targetMs = ComputeTargetProgressMs(state, localNow);
        long drift = Math.Abs(_interpolatedProgressMs - targetMs);
        if (drift > 1500 || !_transport.IsPlaying)
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
        _transport.ClearSeekAnchor();

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

    /// <summary>
    /// Clears the track, lyrics, and theme once the Session ends. Logout also
    /// drops the transport's playback state, so the empty stage asks the user
    /// to connect again.
    /// </summary>
    private void ClearPlayback(bool resetTransport)
    {
        CurrentTitle = "No Track Playing";
        CurrentArtist = "Play music on Spotify to begin";
        CurrentAlbum = string.Empty;
        AlbumArtUrl = null;
        IsPlaying = false;
        if (resetTransport)
        {
            _transport.Reset();
        }
        _lastLyrics = null;
        LyricLines.Clear();
        HasLyrics = false;
        IsStaticLyricsMode = false;
        ActiveLineIndex = -1;
        _themeManager.UpdateTrackMetadata(null, null, null);
        if (resetTransport)
        {
            NotifyEmptyStateText();
        }
        OnPropertyChanged(nameof(HasSyncedLyrics));
        OnPropertyChanged(nameof(HasPlainLyrics));
        OnPropertyChanged(nameof(ModeToggleVisibility));
    }

    private void OnDiagnosticsReceived(DiagnosticsPayload? diag)
    {
        if (diag is null) return;

        PollerStatus = diag.PollerStatus ?? "Idle";
        ConnectedClients = diag.ConnectedClients;
        ActivePollIntervalMs = diag.ActivePollIntervalMs;
        _session.ApplyDiagnostics(diag);
    }

    private void OnTick(object? sender, object e)
    {
        AdvancePlayback();
    }

    /// <summary>
    /// One frame of the display clock: advances the interpolated position and
    /// derives the progress bar, active line, karaoke underline, and
    /// instrumental-break countdown from it.
    /// </summary>
    internal void AdvancePlayback()
    {
        PlaybackStatePayload? state = _transport.PlaybackState;
        if (state?.CurrentTrack is null)
        {
            ProgressFraction = 0;
            ProgressText = "00:00";
            TotalDurationText = "00:00";
            IsInstrumentalBreak = false;
            return;
        }

        TrackInfoPayload track = state.CurrentTrack;
        long durationMs = track.DurationMs;
        if (durationMs <= 0) durationMs = 1;

        long localNow = GetServerNowMs();

        if (state.IsPlaying)
        {
            long dt = _lastTickTimestampMs > 0 ? Math.Clamp(localNow - _lastTickTimestampMs, 1, 100) : 16;
            _lastTickTimestampMs = localNow;

            long targetMs = ComputeTargetProgressMs(state, localNow);

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
            _interpolatedProgressMs = ComputeTargetProgressMs(state, localNow);
            _lastTickTimestampMs = localNow;
        }

        long currentWithOffset = _transport.HoldAtSeekFloor(
            _timing.ComputeRenderPositionMs(_interpolatedProgressMs, durationMs));

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

    public Task NudgeOffsetAsync(int deltaMs) => _timing.NudgeOffsetAsync(deltaMs);

    public Task ResetOffsetAsync() => _timing.ResetOffsetAsync();

    public Task SubscribeToUserAsync(string? userId) => _session.SubscribeToUserAsync(userId);

    public Task LogoutAsync() => _session.LogoutAsync();

    private static string FormatTime(long ms)
    {
        TimeSpan ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
