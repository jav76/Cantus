using System;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Cantus.Client.Views;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;

namespace Cantus.Client;

public sealed partial class MainPage : Page
{
    public LyricsViewModel ViewModel { get; }

    public MainPage()
    {
        var client = new SignalRPlaybackClient();
        ViewModel = new LyricsViewModel(client, ThemeManager.Instance);
        this.InitializeComponent();

        this.Loaded += async (s, e) =>
        {
            await ViewModel.InitializeAsync();
        };

        ViewModel.ActiveLineChanged += OnActiveLineChanged;

        this.KeyDown += OnPageKeyDown;
    }

    private void OnActiveLineChanged(int idx)
    {
        if (idx >= 0 && idx < ViewModel.LyricLines.Count)
        {
            var activeItem = ViewModel.LyricLines[idx];
            if (!ViewModel.IsKioskMode)
            {
                LyricsListView.ScrollIntoView(activeItem);
            }
            else
            {
                KioskLyricsListView.ScrollIntoView(activeItem);
            }
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F11 || e.Key == VirtualKey.K)
        {
            ViewModel.ToggleKioskMode();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.T)
        {
            ViewModel.Theme.CycleNextTheme();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && ViewModel.IsKioskMode)
        {
            ViewModel.IsKioskMode = false;
            e.Handled = true;
        }
    }

    private async void OnConnectSpotifyClicked(object sender, RoutedEventArgs e)
    {
#if __WASM__
        var uri = new Uri("/api/auth/spotify/login", UriKind.RelativeOrAbsolute);
#else
        var uri = new Uri("http://localhost:5000/api/auth/spotify/login");
#endif
        await Launcher.LaunchUriAsync(uri);
    }

    private void OnCycleThemeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.Theme.CycleNextTheme();
    }

    private async void OnOpenDiagnosticsHudClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new DiagnosticsHudDialog(ViewModel);
        if (this.XamlRoot != null)
        {
            dialog.XamlRoot = this.XamlRoot;
        }
        await dialog.ShowAsync();
    }

    private void OnToggleKioskClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleKioskMode();
    }

    private async void OnNudgeMinus500Clicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.NudgeOffsetAsync(-500);
    }

    private async void OnNudgeMinus100Clicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.NudgeOffsetAsync(-100);
    }

    private async void OnNudgePlus100Clicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.NudgeOffsetAsync(100);
    }

    private async void OnNudgePlus500Clicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.NudgeOffsetAsync(500);
    }

    private async void OnResetOffsetClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.ResetOffsetAsync();
    }

    private async void OnSessionItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorizedSessionPayload session)
        {
            await ViewModel.SubscribeToUserAsync(session.Id);
        }
    }

    public SolidColorBrush GetStatusColor(string status)
    {
        return status switch
        {
            "Connected" => new SolidColorBrush(Color.FromArgb(255, 16, 185, 129)),
            "Reconnecting" => new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)),
            _ => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
        };
    }

    public string GetRttText(long rtt) => $"RTT: {rtt}ms";
    public string GetSkewText(long skew) => $"Skew: {(skew >= 0 ? "+" : "")}{skew}ms";
    public string GetPlaybackStatus(bool isPlaying) => isPlaying ? "Playing" : "Paused";

    public static Visibility GetPlayingVisibility(bool isPlaying)
        => isPlaying ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GetEmptyStateVisibility(bool hasLyrics)
        => hasLyrics ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GetLyricsVisibility(bool hasLyrics)
        => hasLyrics ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GetInstrumentalVisibility(bool isInstrumentalBreak)
        => isInstrumentalBreak ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GetStandardViewVisibility(bool isKioskMode)
        => isKioskMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GetKioskViewVisibility(bool isKioskMode)
        => isKioskMode ? Visibility.Visible : Visibility.Collapsed;

    public static double GetKioskFontSize(bool isActive) => isActive ? 44.0 : 28.0;

    public static SolidColorBrush GetLineColor(bool isActive, bool isPast)
    {
        if (isActive) return new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        if (isPast) return new SolidColorBrush(Color.FromArgb(255, 100, 116, 139));
        return new SolidColorBrush(Color.FromArgb(255, 203, 213, 225));
    }
}
