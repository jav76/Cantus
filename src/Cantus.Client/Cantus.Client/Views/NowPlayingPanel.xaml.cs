using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Cantus.Client.Views;

/// <summary>The player part of the track card: art, track text, seek bar, transport, volume and playback state.</summary>
public sealed partial class NowPlayingPanel : UserControl
{
    private const double SEEK_BAR_HEIGHT = 6.0;
    private const double SEEK_BAR_HOVER_HEIGHT = 8.0;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(NowPlayingPanel),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// The host's scroller around this panel. Square art sizing reads its scrollbar and extent,
    /// so the art does not flip the scrollbar on and off. Null when the host does not scroll.
    /// </summary>
    public static readonly DependencyProperty ScrollerProperty =
        DependencyProperty.Register(
            nameof(Scroller),
            typeof(ScrollViewer),
            typeof(NowPlayingPanel),
            new PropertyMetadata(null, OnScrollerChanged));

    public ScrollViewer? Scroller
    {
        get => (ScrollViewer?)GetValue(ScrollerProperty);
        set => SetValue(ScrollerProperty, value);
    }

    public static readonly DependencyProperty TransportProperty =
        DependencyProperty.Register(
            nameof(Transport),
            typeof(PlaybackTransport),
            typeof(NowPlayingPanel),
            new PropertyMetadata(null));

    public PlaybackTransport? Transport
    {
        get => (PlaybackTransport?)GetValue(TransportProperty);
        private set => SetValue(TransportProperty, value);
    }

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(
            nameof(Session),
            typeof(SessionViewModel),
            typeof(NowPlayingPanel),
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
            typeof(NowPlayingPanel),
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
            typeof(NowPlayingPanel),
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
            typeof(NowPlayingPanel),
            new PropertyMetadata(null));

    public ResponsiveLayoutManager? Layout
    {
        get => (ResponsiveLayoutManager?)GetValue(LayoutProperty);
        private set => SetValue(LayoutProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        NowPlayingPanel panel = (NowPlayingPanel)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        panel.Transport = viewModel?.Transport;
        panel.Session = viewModel?.Session;
        panel.Pairing = viewModel?.Pairing;
        panel.Theme = viewModel?.Theme;
        panel.Layout = viewModel?.Layout;
    }

    private static void OnScrollerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        NowPlayingPanel panel = (NowPlayingPanel)d;
        if (e.OldValue is ScrollViewer oldScroller)
        {
            oldScroller.SizeChanged -= panel.OnScrollerSizeChanged;
        }

        if (e.NewValue is ScrollViewer newScroller)
        {
            newScroller.SizeChanged += panel.OnScrollerSizeChanged;
        }
    }

    public NowPlayingPanel()
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

    /// <summary>Gap between the player's sections, shared with the host card's own sections.</summary>
    public static double SectionSpacingFor(LayoutBreakpoint breakpoint) => breakpoint switch
    {
        LayoutBreakpoint.Medium => 12.0,
        _ => 18.0
    };

    public double GetSectionSpacing(LayoutBreakpoint? breakpoint = null)
        => SectionSpacingFor(breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint);

    private async void OnShuffleClicked(object sender, RoutedEventArgs e)
    {
        if (Transport is not null) await Transport.ToggleShuffleAsync();
    }

    private async void OnRepeatClicked(object sender, RoutedEventArgs e)
    {
        if (Transport is not null) await Transport.CycleRepeatAsync();
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

    private void OnSeekBarPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SeekHoverThumb.Visibility = Visibility.Visible;
        SeekProgressBar.Height = SEEK_BAR_HOVER_HEIGHT;
        ViewModel?.VuMeter.SetPointerOver(true);
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        PositionSeekThumb(sender, e);
    }

    private void OnSeekBarPointerExited(object sender, PointerRoutedEventArgs e)
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

    private void PositionSeekThumb(object sender, PointerRoutedEventArgs e)
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

    private async void OnProgressBarPointerReleased(object sender, PointerRoutedEventArgs e)
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

    private void OnAlbumArtFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAlbumArtHeight(e.NewSize.Width);
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAlbumArtHeight(AlbumArtFrame.ActualWidth);
    }

    private void UpdateAlbumArtHeight(double width)
    {
        ScrollViewer? scroller = Scroller;
        double? height = SquareArtSizing.GetNextHeight(
            AlbumArtFrame.Height,
            width,
            scroller?.ComputedVerticalScrollBarVisibility == Visibility.Visible,
            scroller?.ExtentHeight ?? 0,
            scroller?.ViewportHeight ?? double.PositiveInfinity);
        if (height is double next)
        {
            AlbumArtFrame.Height = next;
        }
    }

    /// <summary>Volume is owner-only and has no effect on a local player.</summary>
    public Visibility GetSpotifyControlsVisibility(Visibility ownerControls, Visibility spotifyControls) =>
        ownerControls == Visibility.Visible && spotifyControls == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

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
}
