using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class MobileSettingsView : UserControl
{
    private static readonly ILogger<MobileSettingsView> _logger = ClientLoggingManager.CreateLogger<MobileSettingsView>();
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(MobileSettingsView),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty TimingProperty =
        DependencyProperty.Register(
            nameof(Timing),
            typeof(PlaybackTiming),
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public PlaybackTiming? Timing
    {
        get => (PlaybackTiming?)GetValue(TimingProperty);
        private set => SetValue(TimingProperty, value);
    }

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(
            nameof(Session),
            typeof(SessionViewModel),
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public SessionViewModel? Session
    {
        get => (SessionViewModel?)GetValue(SessionProperty);
        private set => SetValue(SessionProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        MobileSettingsView settings = (MobileSettingsView)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        settings.Timing = viewModel?.Timing;
        settings.Session = viewModel?.Session;
        settings.Theme = viewModel?.Theme;
    }

    public MobileSettingsView()
    {
        InitializeComponent();
    }

    private void OnCycleThemeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Theme.CycleNextTheme();
    }

    private async void OnNudgeMinus500Clicked(object sender, RoutedEventArgs e)
    {
        if (Timing is not null) await Timing.NudgeOffsetAsync(-500);
    }

    private async void OnNudgeMinus100Clicked(object sender, RoutedEventArgs e)
    {
        if (Timing is not null) await Timing.NudgeOffsetAsync(-100);
    }

    private async void OnResetOffsetClicked(object sender, RoutedEventArgs e)
    {
        if (Timing is not null) await Timing.ResetOffsetAsync();
    }

    private async void OnNudgePlus100Clicked(object sender, RoutedEventArgs e)
    {
        if (Timing is not null) await Timing.NudgeOffsetAsync(100);
    }

    private async void OnNudgePlus500Clicked(object sender, RoutedEventArgs e)
    {
        if (Timing is not null) await Timing.NudgeOffsetAsync(500);
    }

    private void OnLatencyMinusClicked(object sender, RoutedEventArgs e)
    {
        Timing?.AdjustLatencyCompensation(-50);
    }

    private void OnLatencyPlusClicked(object sender, RoutedEventArgs e)
    {
        Timing?.AdjustLatencyCompensation(50);
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
            await Windows.System.Launcher.LaunchUriAsync(uri);
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

    public string GetRttText(long? rtt = null) => $"{rtt.GetValueOrDefault()}ms";
    public string GetSkewText(long? skew = null)
    {
        long s = skew.GetValueOrDefault();
        return $"{(s >= 0 ? "+" : "")}{s}ms";
    }
}
