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
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty TransportProperty =
        DependencyProperty.Register(
            nameof(Transport),
            typeof(PlaybackTransport),
            typeof(AdaptiveTrackCard),
            new PropertyMetadata(null));

    public PlaybackTransport? Transport
    {
        get => (PlaybackTransport?)GetValue(TransportProperty);
        private set => SetValue(TransportProperty, value);
    }

    public static readonly DependencyProperty TimingProperty =
        DependencyProperty.Register(
            nameof(Timing),
            typeof(PlaybackTiming),
            typeof(AdaptiveTrackCard),
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
            typeof(AdaptiveTrackCard),
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
            typeof(AdaptiveTrackCard),
            new PropertyMetadata(null));

    public DisplayPairingViewModel? Pairing
    {
        get => (DisplayPairingViewModel?)GetValue(PairingProperty);
        private set => SetValue(PairingProperty, value);
    }

    public static readonly DependencyProperty LyricsPickerProperty =
        DependencyProperty.Register(
            nameof(LyricsPicker),
            typeof(LyricsPickerViewModel),
            typeof(AdaptiveTrackCard),
            new PropertyMetadata(null));

    public LyricsPickerViewModel? LyricsPicker
    {
        get => (LyricsPickerViewModel?)GetValue(LyricsPickerProperty);
        private set => SetValue(LyricsPickerProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(AdaptiveTrackCard),
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
            typeof(AdaptiveTrackCard),
            new PropertyMetadata(null));

    public ResponsiveLayoutManager? Layout
    {
        get => (ResponsiveLayoutManager?)GetValue(LayoutProperty);
        private set => SetValue(LayoutProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        AdaptiveTrackCard card = (AdaptiveTrackCard)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        card.Transport = viewModel?.Transport;
        card.Timing = viewModel?.Timing;
        card.Session = viewModel?.Session;
        card.Pairing = viewModel?.Pairing;
        card.LyricsPicker = viewModel?.LyricsPicker;
        card.Theme = viewModel?.Theme;
        card.Layout = viewModel?.Layout;
    }

    public AdaptiveTrackCard()
    {
        InitializeComponent();
        SeekArea.RegisterPropertyChangedCallback(IsHitTestVisibleProperty, (s, dp) =>
        {
            if (!SeekArea.IsHitTestVisible)
            {
                EndSeekHover();
            }
        });
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

    private async void OnWrongLyricsClicked(object sender, RoutedEventArgs e)
    {
        if (LyricsPicker is not null)
        {
            await LyricsPickerDialog.ShowForAsync(LyricsPicker, XamlRoot);
        }
    }

    private async void OnShuffleClicked(object sender, RoutedEventArgs e)
    {
        if (Transport is not null) await Transport.ToggleShuffleAsync();
    }

    private async void OnRepeatClicked(object sender, RoutedEventArgs e)
    {
        if (Transport is not null) await Transport.CycleRepeatAsync();
    }

    private const double SEEK_BAR_HEIGHT = 6.0;
    private const double SEEK_BAR_HOVER_HEIGHT = 8.0;

    private void OnSeekBarPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SeekHoverThumb.Visibility = Visibility.Visible;
        SeekProgressBar.Height = SEEK_BAR_HOVER_HEIGHT;
        ViewModel?.VuMeter.SetPointerOver(true);
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        EndSeekHover();
    }

    // Also runs when the bar stops being clickable under the pointer, which raises no PointerExited.
    private void EndSeekHover()
    {
        SeekHoverThumb.Visibility = Visibility.Collapsed;
        SeekProgressBar.Height = SEEK_BAR_HEIGHT;
        ViewModel?.VuMeter.SetPointerOver(false);
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
        if (Transport is null || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        double fraction = e.GetCurrentPoint(element).Position.X / element.ActualWidth;
        await Transport.SeekToFractionAsync(fraction);
    }

    private void OnVolumeSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (Transport is null)
        {
            return;
        }

        int requested = (int)Math.Round(e.NewValue);

        // ValueChanged also fires when the binding pushes a state update into
        // the slider; only user-initiated changes differ from the bound value.
        if (requested == (int)Math.Round(Transport.VolumeSliderValue))
        {
            return;
        }

        Transport.RequestVolumeChange(requested);
    }

    private async void OnSkipPreviousClicked(object sender, RoutedEventArgs e)
    {
        if (Transport is not null) await Transport.SkipToPreviousAsync();
    }

    private async void OnTogglePlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (Transport is not null) await Transport.TogglePlayPauseAsync();
    }

    private async void OnSkipNextClicked(object sender, RoutedEventArgs e)
    {
        if (Transport is not null) await Transport.SkipToNextAsync();
    }

    private async void OnConnectSpotifyClicked(object sender, RoutedEventArgs e)
    {
        await SpotifySignIn.StartAsync(Session, XamlRoot);
    }

    private async void OnPairAsDisplayClicked(object sender, RoutedEventArgs e)
    {
        if (Pairing is not null)
        {
            await Pairing.StartPairingAsync();
        }
    }

    private async void OnLogoutClicked(object sender, RoutedEventArgs e)
    {
        if (Session is not null)
        {
            await Session.LogoutAsync();
        }
    }

    /// <summary>The "Wrong lyrics?" button is owner-only, so it never shows on a Display.</summary>
    public Visibility GetLyricsPickerVisibility(Visibility ownerControls, Visibility pickerAction) =>
        ownerControls == Visibility.Visible && pickerAction == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnAlbumArtFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAlbumArtHeight(e.NewSize.Width);
    }

    private void OnStandardCardScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAlbumArtHeight(AlbumArtFrame.ActualWidth);
    }

    private void UpdateAlbumArtHeight(double width)
    {
        double? height = SquareArtSizing.GetNextHeight(
            AlbumArtFrame.Height,
            width,
            StandardCardScroller.ComputedVerticalScrollBarVisibility == Visibility.Visible,
            StandardCardScroller.ExtentHeight,
            StandardCardScroller.ViewportHeight);
        if (height is double next)
        {
            AlbumArtFrame.Height = next;
        }
    }

    public Visibility GetNarrowPhoneHiddenVisibility(bool isNarrowPhone)
        => isNarrowPhone ? Visibility.Collapsed : Visibility.Visible;

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
            LayoutBreakpoint.Medium => 20.0,
            _ => 24.0
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
