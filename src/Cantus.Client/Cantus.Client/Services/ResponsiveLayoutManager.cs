using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cantus.Client.Models;
using Microsoft.UI.Xaml;

namespace Cantus.Client.Services;

/// <summary>
/// Core responsive layout engine for Cantus.
/// Dynamically tracks viewport dimensions, calculates active breakpoints, and provides
/// continuous adaptive values for smooth multi-form-factor scaling across mobile, tablet,
/// desktop, and 10-foot TV / kiosk displays.
/// </summary>
public sealed class ResponsiveLayoutManager : INotifyPropertyChanged
{
    private static ResponsiveLayoutManager? _instance;
    public static ResponsiveLayoutManager Instance => _instance ??= new ResponsiveLayoutManager();

    // Breakpoint thresholds (in logical pixels)
    public const double SMALL_BREAKPOINT_MAX_WIDTH = 680.0;
    public const double MEDIUM_BREAKPOINT_MAX_WIDTH = 1080.0;
    public const double LARGE_BREAKPOINT_MAX_WIDTH = 1920.0;

    private double _windowWidth = 1280.0;
    private double _windowHeight = 800.0;
    private bool _isKioskMode;
    private LayoutBreakpoint? _breakpointOverride;
    private LayoutBreakpoint _currentBreakpoint = LayoutBreakpoint.Large;
    private LayoutOrientation _orientation = LayoutOrientation.Landscape;
    private MobileViewMode _mobileView = MobileViewMode.Lyrics;

    public double WindowWidth
    {
        get => _windowWidth;
        private set
        {
            if (Math.Abs(_windowWidth - value) > 0.5)
            {
                _windowWidth = value;
                OnPropertyChanged();
            }
        }
    }

    public double WindowHeight
    {
        get => _windowHeight;
        private set
        {
            if (Math.Abs(_windowHeight - value) > 0.5)
            {
                _windowHeight = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsKioskMode
    {
        get => _isKioskMode;
        set
        {
            if (_isKioskMode != value)
            {
                _isKioskMode = value;
                OnPropertyChanged();
                RecalculateLayout();
            }
        }
    }

    public LayoutBreakpoint? BreakpointOverride
    {
        get => _breakpointOverride;
        set
        {
            if (_breakpointOverride != value)
            {
                _breakpointOverride = value;
                OnPropertyChanged();
                RecalculateLayout();
            }
        }
    }

    public LayoutBreakpoint CurrentBreakpoint
    {
        get => _currentBreakpoint;
        private set
        {
            if (_currentBreakpoint != value)
            {
                _currentBreakpoint = value;
                OnPropertyChanged();
                NotifyBreakpointDependentProperties();
                BreakpointChanged?.Invoke(value);
            }
        }
    }

    public LayoutOrientation Orientation
    {
        get => _orientation;
        private set
        {
            if (_orientation != value)
            {
                _orientation = value;
                OnPropertyChanged();
                NotifyBreakpointDependentProperties();
            }
        }
    }

    public MobileViewMode MobileView
    {
        get => _mobileView;
        set
        {
            if (_mobileView != value)
            {
                _mobileView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMobileLyricsActive));
                OnPropertyChanged(nameof(IsMobileNowPlayingActive));
                OnPropertyChanged(nameof(IsMobileSettingsActive));
                LayoutChanged?.Invoke();
            }
        }
    }

    // Convenience boolean layout flags for XAML binding
    public bool IsSmall => CurrentBreakpoint == LayoutBreakpoint.Small;
    public bool IsMedium => CurrentBreakpoint == LayoutBreakpoint.Medium;
    public bool IsLarge => CurrentBreakpoint == LayoutBreakpoint.Large;
    public bool IsFullscreenTv => CurrentBreakpoint == LayoutBreakpoint.FullscreenTv;
    public bool IsCompact => IsSmall || (IsMedium && Orientation == LayoutOrientation.Portrait);
    public bool IsWide => IsLarge || IsFullscreenTv;

    // Mobile view tab flags
    public bool IsMobileLyricsActive => IsSmall && MobileView == MobileViewMode.Lyrics;
    public bool IsMobileNowPlayingActive => IsSmall && MobileView == MobileViewMode.NowPlaying;
    public bool IsMobileSettingsActive => IsSmall && MobileView == MobileViewMode.SyncAndSettings;

    // Structural visibility & sizing properties
    public bool ShowSidebar => (IsLarge || (IsMedium && Orientation == LayoutOrientation.Landscape)) && !IsFullscreenTv;
    public bool ShowTopHeader => !IsFullscreenTv;
    public bool ShowMobileTabBar => IsSmall && !IsFullscreenTv;
    public bool ShowMiniBottomBar => IsFullscreenTv;

    public double SidePanelWidth => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => 0.0,
        LayoutBreakpoint.Medium => (Orientation == LayoutOrientation.Landscape ? 290.0 : 0.0),
        LayoutBreakpoint.Large => 380.0,
        _ => 0.0
    };

    public double AlbumArtSize => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => (MobileView == MobileViewMode.NowPlaying ? 240.0 : 64.0),
        LayoutBreakpoint.Medium => 230.0,
        LayoutBreakpoint.Large => 332.0,
        _ => 56.0
    };

    public double ActiveLyricsFontSize => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => 24.0,
        LayoutBreakpoint.Medium => 32.0,
        LayoutBreakpoint.Large => 38.0,
        LayoutBreakpoint.FullscreenTv => 50.0,
        _ => 36.0
    };

    public double InactiveLyricsFontSize => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => 16.0,
        LayoutBreakpoint.Medium => 20.0,
        LayoutBreakpoint.Large => 23.0,
        LayoutBreakpoint.FullscreenTv => 30.0,
        _ => 22.0
    };

    public double PastLyricsFontSize => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => 15.0,
        LayoutBreakpoint.Medium => 18.0,
        LayoutBreakpoint.Large => 20.0,
        LayoutBreakpoint.FullscreenTv => 26.0,
        _ => 20.0
    };

    public double LyricsLineSpacing => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => 8.0,
        LayoutBreakpoint.Medium => 12.0,
        LayoutBreakpoint.Large => 14.0,
        LayoutBreakpoint.FullscreenTv => 20.0,
        _ => 12.0
    };

    public Thickness ContentPadding => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => new Thickness(12, 10, 12, 12),
        LayoutBreakpoint.Medium => new Thickness(18, 14, 18, 16),
        LayoutBreakpoint.Large => new Thickness(24, 20, 24, 24),
        LayoutBreakpoint.FullscreenTv => new Thickness(48, 32, 48, 32),
        _ => new Thickness(24)
    };

    public double LyricsMaxWidth => CurrentBreakpoint switch
    {
        LayoutBreakpoint.FullscreenTv => 1100.0,
        _ => double.PositiveInfinity
    };

    public HeaderDisplayMode HeaderMode => CurrentBreakpoint switch
    {
        LayoutBreakpoint.Small => HeaderDisplayMode.Minimal,
        LayoutBreakpoint.Medium => HeaderDisplayMode.Compact,
        LayoutBreakpoint.Large => HeaderDisplayMode.Full,
        _ => HeaderDisplayMode.Hidden
    };

    public event Action<LayoutBreakpoint>? BreakpointChanged;
    public event Action? LayoutChanged;

    public ResponsiveLayoutManager()
    {
        RecalculateLayout();
    }

    /// <summary>
    /// Updates the window/viewport size and triggers dynamic recalculation of layout parameters.
    /// </summary>
    public void UpdateDimensions(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        WindowWidth = width;
        WindowHeight = height;
        Orientation = width >= height ? LayoutOrientation.Landscape : LayoutOrientation.Portrait;

        RecalculateLayout();
    }

    /// <summary>
    /// Evaluates current dimensions, kiosk mode, and overrides to compute layout state.
    /// </summary>
    public void RecalculateLayout()
    {
        LayoutBreakpoint computed;

        if (BreakpointOverride.HasValue)
        {
            computed = BreakpointOverride.Value;
        }
        else if (IsKioskMode)
        {
            computed = LayoutBreakpoint.FullscreenTv;
        }
        else if (WindowWidth < SMALL_BREAKPOINT_MAX_WIDTH)
        {
            computed = LayoutBreakpoint.Small;
        }
        else if (WindowWidth < MEDIUM_BREAKPOINT_MAX_WIDTH)
        {
            computed = LayoutBreakpoint.Medium;
        }
        else if (WindowWidth < LARGE_BREAKPOINT_MAX_WIDTH)
        {
            computed = LayoutBreakpoint.Large;
        }
        else
        {
            computed = LayoutBreakpoint.FullscreenTv;
        }

        CurrentBreakpoint = computed;
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// Cycles mobile view mode between Lyrics, NowPlaying, and SyncAndSettings.
    /// </summary>
    public void CycleMobileView()
    {
        MobileView = MobileView switch
        {
            MobileViewMode.Lyrics => MobileViewMode.NowPlaying,
            MobileViewMode.NowPlaying => MobileViewMode.SyncAndSettings,
            _ => MobileViewMode.Lyrics
        };
    }

    private void NotifyBreakpointDependentProperties()
    {
        OnPropertyChanged(nameof(IsSmall));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsLarge));
        OnPropertyChanged(nameof(IsFullscreenTv));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(IsWide));
        OnPropertyChanged(nameof(ShowSidebar));
        OnPropertyChanged(nameof(ShowTopHeader));
        OnPropertyChanged(nameof(ShowMobileTabBar));
        OnPropertyChanged(nameof(ShowMiniBottomBar));
        OnPropertyChanged(nameof(SidePanelWidth));
        OnPropertyChanged(nameof(AlbumArtSize));
        OnPropertyChanged(nameof(ActiveLyricsFontSize));
        OnPropertyChanged(nameof(InactiveLyricsFontSize));
        OnPropertyChanged(nameof(PastLyricsFontSize));
        OnPropertyChanged(nameof(LyricsLineSpacing));
        OnPropertyChanged(nameof(ContentPadding));
        OnPropertyChanged(nameof(LyricsMaxWidth));
        OnPropertyChanged(nameof(HeaderMode));
        OnPropertyChanged(nameof(IsMobileLyricsActive));
        OnPropertyChanged(nameof(IsMobileNowPlayingActive));
        OnPropertyChanged(nameof(IsMobileSettingsActive));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
