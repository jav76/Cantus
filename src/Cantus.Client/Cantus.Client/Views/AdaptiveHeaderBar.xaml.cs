using System;
using System.ComponentModel;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cantus.Client.Views;

public sealed partial class AdaptiveHeaderBar : UserControl
{
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
            header.SubscribeToViewModel(viewModel);
        }
    }

    private LyricsViewModel? _subscribedViewModel;
    private bool _isLoaded;

    public AdaptiveHeaderBar()
    {
        this.InitializeComponent();

        // Radio groups are tracked by name across the app, so each header gets its own names.
        string groupPrefix = $"CantusTheme-{Guid.NewGuid():N}";
        ThemeMenuItems.Populate(LargeThemeMenu.Items, $"{groupPrefix}-Large", OnThemeSelected, OnEffectToggled);
        ThemeMenuItems.Populate(MediumThemeMenu.Items, $"{groupPrefix}-Medium", OnThemeSelected, OnEffectToggled);
        ThemeMenuItems.Populate(SmallThemeMenu.Items, $"{groupPrefix}-Small", OnThemeSelected, OnEffectToggled);

        Loaded += (s, e) =>
        {
            _isLoaded = true;
            SubscribeToViewModel(ViewModel);
            SyncThemeMenus();
        };
        Unloaded += (s, e) =>
        {
            _isLoaded = false;
            SubscribeToViewModel(null);
        };
    }

    private void OnThemeSelected(ThemeMode mode)
    {
        ViewModel?.Theme.SetThemeMode(mode);
    }

    private void OnEffectToggled(VisualEffect effect, bool isEnabled)
    {
        ViewModel?.Effects.SetEnabled(effect, isEnabled);
    }

    private void OnThemeMenuOpening(object sender, object e)
    {
        SyncThemeMenus();
    }

    private void SubscribeToViewModel(LyricsViewModel? viewModel)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.Effects.PropertyChanged -= OnEffectsChanged;
            _subscribedViewModel.Theme.PropertyChanged -= OnThemeStateChanged;
        }

        _subscribedViewModel = viewModel;

        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.Effects.PropertyChanged += OnEffectsChanged;
            viewModel.Theme.PropertyChanged += OnThemeStateChanged;
        }

        ApplyStatusTooltip();
        ApplyAlertChips();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LyricsViewModel.RttMs):
                ApplyStatusTooltip();
                break;
            case nameof(LyricsViewModel.ConnectionStatus):
            case nameof(LyricsViewModel.PollerStatus):
            case null:
            case "":
                ApplyStatusTooltip();
                ApplyAlertChips();
                break;
        }
    }

    // Set from code rather than x:Bind: when one function binding with FallbackValue shares ViewModel.*
    // parameter paths with function bindings without one, the WinUI XAML compiler emits calls to
    // UpdateFallback_M_* methods it never generates (CS1061 on the Windows head only).
    private void ApplyStatusTooltip()
    {
        LyricsViewModel? viewModel = _subscribedViewModel;
        string? tooltip = viewModel is null
            ? null
            : HeaderStatus.GetTooltip(viewModel.ConnectionStatus, viewModel.RttMs, viewModel.PollerStatus);

        foreach (Border indicator in new[] { LargeStatusIndicator, MediumStatusIndicator, SmallStatusIndicator })
        {
            ToolTipService.SetToolTip(indicator, tooltip);
            AutomationProperties.SetName(indicator, tooltip ?? string.Empty);
        }

        ToolTipService.SetToolTip(LargeAlertChip, tooltip);
        ToolTipService.SetToolTip(MediumAlertChip, tooltip);
    }

    private void ApplyAlertChips()
    {
        LyricsViewModel? viewModel = _subscribedViewModel;
        string? alertText = viewModel is null
            ? null
            : HeaderStatus.GetAlertText(viewModel.ConnectionStatus, viewModel.PollerStatus);
        Visibility visibility = alertText is null ? Visibility.Collapsed : Visibility.Visible;

        LargeAlertChip.Visibility = visibility;
        MediumAlertChip.Visibility = visibility;
        LargeAlertText.Text = alertText ?? string.Empty;
        MediumAlertText.Text = alertText ?? string.Empty;

        if (viewModel is not null)
        {
            StatusTone tone = HeaderStatus.GetAlertTone(viewModel.ConnectionStatus);
            LargeAlertDot.Fill = CreateToneBrush(tone);
            MediumAlertDot.Fill = CreateToneBrush(tone);
        }
    }

    private void OnThemeStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeManager.CurrentMode))
        {
            SyncThemeMenus();
        }
    }

    private void OnEffectsChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncThemeMenus();
    }

    private void SyncThemeMenus()
    {
        if (ViewModel is null) return;
        ThemeMode mode = ViewModel.Theme.CurrentMode;
        VisualEffectsViewModel effects = ViewModel.Effects;
        ThemeMenuItems.Sync(LargeThemeMenu.Items, mode, effects);
        ThemeMenuItems.Sync(MediumThemeMenu.Items, mode, effects);
        ThemeMenuItems.Sync(SmallThemeMenu.Items, mode, effects);
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
        await SpotifySignIn.StartAsync(Session, XamlRoot);
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

    /// <summary>
    /// The header has no card, so it only keeps a gap above its hairline. With no side padding
    /// the brand lines up with the sidebar's left edge and the buttons with the page's right edge.
    /// </summary>
    public Thickness GetHeaderPadding(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => new Thickness(0, 0, 0, 8),
            LayoutBreakpoint.Medium => new Thickness(0, 0, 0, 10),
            _ => new Thickness(0, 0, 0, 12)
        };
    }

    private static readonly Color HealthyColor = Color.FromArgb(255, 16, 185, 129);
    private static readonly Color WarningColor = Color.FromArgb(255, 245, 158, 11);
    private static readonly Color ErrorColor = Color.FromArgb(255, 239, 68, 68);

    public SolidColorBrush GetStatusColor(string? status)
        => CreateToneBrush(HeaderStatus.GetStatusTone(status));

    private static SolidColorBrush CreateToneBrush(StatusTone tone) => tone switch
    {
        StatusTone.Healthy => new SolidColorBrush(HealthyColor),
        StatusTone.Warning => new SolidColorBrush(WarningColor),
        _ => new SolidColorBrush(ErrorColor)
    };
}
