using System;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class MiniPlaybackBar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(MiniPlaybackBar),
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
            typeof(MiniPlaybackBar),
            new PropertyMetadata(null));

    public PlaybackTransport? Transport
    {
        get => (PlaybackTransport?)GetValue(TransportProperty);
        private set => SetValue(TransportProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(MiniPlaybackBar),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        MiniPlaybackBar bar = (MiniPlaybackBar)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        bar.Transport = viewModel?.Transport;
        bar.Theme = viewModel?.Theme;
    }

    public MiniPlaybackBar()
    {
        this.InitializeComponent();
    }

    private const double SEEK_BAR_HEIGHT = 4.0;
    private const double SEEK_BAR_HOVER_HEIGHT = 6.0;

    private async void OnProgressBarPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (Transport is null || sender is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return;
        }

        double fraction = e.GetCurrentPoint(element).Position.X / element.ActualWidth;
        await Transport.SeekToFractionAsync(fraction);
    }

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

    private void OnToggleKioskClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Layout.ToggleKioskMode();
    }
}
