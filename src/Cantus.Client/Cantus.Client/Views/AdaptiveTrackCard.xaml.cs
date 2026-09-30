using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class AdaptiveTrackCard : UserControl
{
    private static readonly ILogger<AdaptiveTrackCard> _logger = ClientLoggingManager.CreateLogger<AdaptiveTrackCard>();
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
        InitializeComponent();
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

    private async void OnShuffleClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.ToggleShuffleAsync();
    }

    private async void OnRepeatClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.CycleRepeatAsync();
    }

    private const double SEEK_BAR_HEIGHT = 6.0;
    private const double SEEK_BAR_HOVER_HEIGHT = 8.0;

    private void OnSeekBarPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SeekHoverThumb.Visibility = Visibility.Visible;
        SeekProgressBar.Height = SEEK_BAR_HOVER_HEIGHT;
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SeekHoverThumb.Visibility = Visibility.Collapsed;
        SeekProgressBar.Height = SEEK_BAR_HEIGHT;
    }

    private void PositionSeekThumb(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        double half = SeekHoverThumb.Width / 2.0;
        double pointerX = e.GetCurrentPoint(element).Position.X;
        SeekHoverThumbTransform.X = Math.Clamp(
            pointerX - half,
            0,
            Math.Max(0, element.ActualWidth - SeekHoverThumb.Width));
    }

    private async void OnProgressBarPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel is null || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        double fraction = e.GetCurrentPoint(element).Position.X / element.ActualWidth;
        await ViewModel.SeekToFractionAsync(fraction);
    }

    private void OnVolumeSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        int requested = (int)Math.Round(e.NewValue);

        // ValueChanged also fires when the binding pushes a state update into
        // the slider; only user-initiated changes differ from the bound value.
        if (requested == (int)Math.Round(ViewModel.VolumeSliderValue))
        {
            return;
        }

        ViewModel.RequestVolumeChange(requested);
    }

    private async void OnSkipPreviousClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.SkipToPreviousAsync();
    }

    private async void OnTogglePlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.TogglePlayPauseAsync();
    }

    private async void OnSkipNextClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.SkipToNextAsync();
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
            string baseUrl = ViewModel?.ServerBaseUrl ?? "http://127.0.0.1:5000";
            Uri uri = new($"{baseUrl}/api/auth/spotify/login{clientQuery}");
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
