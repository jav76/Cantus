using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class AdaptiveTrackCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(AdaptiveTrackCard),
            new PropertyMetadata(null));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public AdaptiveTrackCard()
    {
        this.InitializeComponent();
    }

    private async void OnNudgeMinus500Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.NudgeOffsetAsync(-500);
    }

    private async void OnNudgeMinus100Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.NudgeOffsetAsync(-100);
    }

    private async void OnResetOffsetClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.ResetOffsetAsync();
    }

    private async void OnNudgePlus100Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.NudgeOffsetAsync(100);
    }

    private async void OnNudgePlus500Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.NudgeOffsetAsync(500);
    }

    private void OnToggleCalibrationClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleCalibrationMode();
    }

    public Microsoft.UI.Xaml.Media.SolidColorBrush GetCalibrateButtonBackground(bool isCalibrationMode)
    {
        return isCalibrationMode
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(60, 59, 130, 246))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(32, 255, 255, 255));
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
            await Windows.System.Launcher.LaunchUriAsync(uri);
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdaptiveTrackCard] Error connecting to Spotify: {ex}");
        }
    }

    private async void OnLogoutClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.LogoutAsync();
        }
    }

    public Visibility GetStandardCardVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp != LayoutBreakpoint.Small ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetMobileStripVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.Small ? Visibility.Visible : Visibility.Collapsed;
    }

    public Thickness GetCardPadding(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => new Thickness(12, 10, 12, 10),
            LayoutBreakpoint.Medium => new Thickness(16),
            _ => new Thickness(24)
        };
    }

    public double GetCardSpacing(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Medium => 12.0,
            _ => 18.0
        };
    }

    public double GetAlbumIconSize(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Medium => 48.0,
            _ => 64.0
        };
    }

    public double GetTitleFontSize(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Medium => 18.0,
            _ => 22.0
        };
    }

    public double GetArtistFontSize(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Medium => 13.0,
            _ => 15.0
        };
    }

    public Visibility GetInstrumentalVisibility(bool? isInstrumentalBreak = null)
        => isInstrumentalBreak.GetValueOrDefault() ? Visibility.Visible : Visibility.Collapsed;

    public string GetPlaybackStatus(bool? isPlaying = null) => isPlaying.GetValueOrDefault() ? "Playing" : "Paused";

    public static Visibility GetPlayingVisibility(bool isPlaying)
        => isPlaying ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility GetNoSessionsVisibility(int count)
        => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility GetHasSessionsVisibility(int count)
        => count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
