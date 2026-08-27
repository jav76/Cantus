using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Cantus.Client.Views;

public sealed partial class AdaptiveHeaderBar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(AdaptiveHeaderBar),
            new PropertyMetadata(null));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public AdaptiveHeaderBar()
    {
        this.InitializeComponent();
    }

    private void OnCycleThemeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Theme.CycleNextTheme();
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

    private void OnToggleKioskClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleKioskMode();
    }

    private void OnCycleMobileViewClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.CycleMobileView();
    }

    private async void OnConnectSpotifyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
#if __WASM__
            string origin = WasmInterop.GetCurrentOrigin();
            string loginUrl = !string.IsNullOrWhiteSpace(origin)
                ? $"{origin}/api/auth/spotify/login"
                : "/api/auth/spotify/login";
            WasmInterop.NavigateTo(loginUrl);
#else
            Uri uri = new("http://localhost:5000/api/auth/spotify/login");
            await Launcher.LaunchUriAsync(uri);
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdaptiveHeaderBar] Error connecting to Spotify: {ex}");
        }
    }

    private async void OnLogoutClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.LogoutAsync();
        }
    }

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
