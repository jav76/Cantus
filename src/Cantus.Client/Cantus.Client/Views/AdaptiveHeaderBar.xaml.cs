using System;
using System.ComponentModel;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Cantus.Client.Views;

public sealed partial class AdaptiveHeaderBar : UserControl
{
    private static readonly ILogger<AdaptiveHeaderBar> _logger = ClientLoggingManager.CreateLogger<AdaptiveHeaderBar>();
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(AdaptiveHeaderBar),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(
            nameof(Session),
            typeof(SessionViewModel),
            typeof(AdaptiveHeaderBar),
            new PropertyMetadata(null));

    public SessionViewModel? Session
    {
        get => (SessionViewModel?)GetValue(SessionProperty);
        private set => SetValue(SessionProperty, value);
    }

    public static readonly DependencyProperty PairingProperty =
        DependencyProperty.Register(
            nameof(Pairing),
            typeof(DisplayPairingViewModel),
            typeof(AdaptiveHeaderBar),
            new PropertyMetadata(null));

    public DisplayPairingViewModel? Pairing
    {
        get => (DisplayPairingViewModel?)GetValue(PairingProperty);
        private set => SetValue(PairingProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(AdaptiveHeaderBar),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    public static readonly DependencyProperty LayoutProperty =
        DependencyProperty.Register(
            nameof(Layout),
            typeof(ResponsiveLayoutManager),
            typeof(AdaptiveHeaderBar),
            new PropertyMetadata(null));

    public ResponsiveLayoutManager? Layout
    {
        get => (ResponsiveLayoutManager?)GetValue(LayoutProperty);
        private set => SetValue(LayoutProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        AdaptiveHeaderBar header = (AdaptiveHeaderBar)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        header.Session = viewModel?.Session;
        header.Pairing = viewModel?.Pairing;
        header.Theme = viewModel?.Theme;
        header.Layout = viewModel?.Layout;
        if (header._isLoaded)
        {
            header.SubscribeToThemeState(viewModel);
        }
    }

    private LyricsViewModel? _subscribedViewModel;
    private bool _isLoaded;

    public AdaptiveHeaderBar()
    {
        this.InitializeComponent();

        // Radio groups are tracked by name across the app, so each header gets its own names.
        string groupPrefix = $"CantusTheme-{Guid.NewGuid():N}";
        ThemeMenuItems.Populate(LargeThemeMenu.Items, $"{groupPrefix}-Large", OnThemeSelected, OnPostProcessingToggled);
        ThemeMenuItems.Populate(MediumThemeMenu.Items, $"{groupPrefix}-Medium", OnThemeSelected, OnPostProcessingToggled);
        ThemeMenuItems.Populate(SmallThemeMenu.Items, $"{groupPrefix}-Small", OnThemeSelected, OnPostProcessingToggled);

        Loaded += (s, e) =>
        {
            _isLoaded = true;
            SubscribeToThemeState(ViewModel);
            SyncThemeMenus();
        };
        Unloaded += (s, e) =>
        {
            _isLoaded = false;
            SubscribeToThemeState(null);
        };
    }

    private void OnThemeSelected(ThemeMode mode)
    {
        ViewModel?.Theme.SetThemeMode(mode);
    }

    private void OnPostProcessingToggled(bool isEnabled)
    {
        if (ViewModel is not null)
        {
            ViewModel.IsPostProcessingEnabled = isEnabled;
        }
    }

    private void OnThemeMenuOpening(object sender, object e)
    {
        SyncThemeMenus();
    }

    private void SubscribeToThemeState(LyricsViewModel? viewModel)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnThemeStateChanged;
            _subscribedViewModel.Theme.PropertyChanged -= OnThemeStateChanged;
        }

        _subscribedViewModel = viewModel;

        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnThemeStateChanged;
            viewModel.Theme.PropertyChanged += OnThemeStateChanged;
        }
    }

    private void OnThemeStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThemeManager.CurrentMode) or nameof(LyricsViewModel.IsPostProcessingEnabled))
        {
            SyncThemeMenus();
        }
    }

    private void SyncThemeMenus()
    {
        if (ViewModel is null) return;
        ThemeMode mode = ViewModel.Theme.CurrentMode;
        bool isPostProcessingEnabled = ViewModel.IsPostProcessingEnabled;
        ThemeMenuItems.Sync(LargeThemeMenu.Items, mode, isPostProcessingEnabled);
        ThemeMenuItems.Sync(MediumThemeMenu.Items, mode, isPostProcessingEnabled);
        ThemeMenuItems.Sync(SmallThemeMenu.Items, mode, isPostProcessingEnabled);
    }

    private async void OnOpenDiagnosticsHudClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        DiagnosticsHudDialog dialog = new(ViewModel);
        if (this.XamlRoot is not null)
        {
            dialog.XamlRoot = this.XamlRoot;
        }
        await dialog.ShowAsync();
    }

    private async void OnOpenSongLookupClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        SongLookupDialog dialog = new(ViewModel);
        if (this.XamlRoot is not null)
        {
            dialog.XamlRoot = this.XamlRoot;
        }
        await dialog.ShowAsync();
    }

    private void OnToggleKioskClicked(object sender, RoutedEventArgs e)
    {
        Layout?.ToggleKioskMode();
    }

    private void OnCycleMobileViewClicked(object sender, RoutedEventArgs e)
    {
        Layout?.CycleMobileView();
    }

    private async void OnConnectSpotifyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string loginPath = Session?.SpotifyLoginPath ?? SessionViewModel.SPOTIFY_LOGIN_PATH;
#if __WASM__
            string origin = WasmInterop.GetCurrentOrigin();
            string loginUrl = !string.IsNullOrWhiteSpace(origin)
                ? $"{origin}{loginPath}"
                : loginPath;
            WasmInterop.NavigateTo(loginUrl);
#else
            string baseUrl = Session?.ServerBaseUrl ?? "http://127.0.0.1:5000";
            Uri uri = new($"{baseUrl}{loginPath}");
            await Launcher.LaunchUriAsync(uri);
#endif
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Spotify");
        }
    }

    private async void OnLogoutClicked(object sender, RoutedEventArgs e)
    {
        if (Session is not null)
        {
            await Session.LogoutAsync();
        }
    }

    /// <summary>Find a song is owner-only, so it never shows on a Display, even one paired after a sign-in.</summary>
    public Visibility GetSongLookupVisibility(Visibility ownerControls, Visibility ownerActions) =>
        ownerControls == Visibility.Visible && ownerActions == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility GetLabelVisibility(bool showLabels)
        => showLabels ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GetLargeHeaderVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.Large ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetMediumHeaderVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.Medium ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetSmallHeaderVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.Small ? Visibility.Visible : Visibility.Collapsed;
    }

    public Thickness GetHeaderPadding(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => new Thickness(12, 8, 12, 8),
            LayoutBreakpoint.Medium => new Thickness(14, 10, 14, 10),
            _ => new Thickness(16, 12, 16, 12)
        };
    }

    public SolidColorBrush GetStatusColor(string? status) => status switch
    {
        "Connected" => new SolidColorBrush(Color.FromArgb(255, 16, 185, 129)),
        "Reconnecting" => new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)),
        _ => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
    };

    public string GetRttText(long? rtt = null) => $"RTT: {rtt.GetValueOrDefault()}ms";
    public string GetSkewText(long? skew = null)
    {
        long s = skew.GetValueOrDefault();
        return $"Skew: {(s >= 0 ? "+" : "")}{s}ms";
    }
}
