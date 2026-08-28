using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class MobileSettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
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

    private async void OnConnectSpotifyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string clientId = ViewModel?.ClientId ?? string.Empty;
            string clientQuery = !string.IsNullOrWhiteSpace(clientId) ? $"?client_id={clientId}" : string.Empty;
#if __WASM__
            string origin = WasmInterop.GetCurrentOrigin();
            string loginUrl = !string.IsNullOrWhiteSpace(origin)
                ? $"{origin}/api/auth/spotify/login{clientQuery}"
                : $"/api/auth/spotify/login{clientQuery}";
            WasmInterop.NavigateTo(loginUrl);
#else
            string baseUrl = ViewModel?.ServerBaseUrl ?? "http://localhost:5000";
            Uri uri = new($"{baseUrl}/api/auth/spotify/login{clientQuery}");
            await Windows.System.Launcher.LaunchUriAsync(uri);
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MobileSettingsView] Error connecting to Spotify: {ex}");
        }
    }

    private async void OnLogoutClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.LogoutAsync();
        }
    }

    public string GetRttText(long? rtt = null) => $"{rtt.GetValueOrDefault()}ms";
    public string GetSkewText(long? skew = null)
    {
        long s = skew.GetValueOrDefault();
        return $"{(s >= 0 ? "+" : "")}{s}ms";
    }
}
