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

    public static readonly DependencyProperty PairingProperty =
        DependencyProperty.Register(
            nameof(Pairing),
            typeof(DisplayPairingViewModel),
            typeof(MiniPlaybackBar),
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
            typeof(MiniPlaybackBar),
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
            typeof(MiniPlaybackBar),
            new PropertyMetadata(null, OnLayoutChanged));

    public ResponsiveLayoutManager? Layout
    {
        get => (ResponsiveLayoutManager?)GetValue(LayoutProperty);
        private set => SetValue(LayoutProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        MiniPlaybackBar bar = (MiniPlaybackBar)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        bar.Transport = viewModel?.Transport;
        bar.Pairing = viewModel?.Pairing;
        bar.Theme = viewModel?.Theme;
        bar.Layout = viewModel?.Layout;
    }

    public Visibility GetWideBarVisibility(bool isCompact)
        => isCompact ? Visibility.Collapsed : Visibility.Visible;

    public Thickness GetProgressMargin(bool isCompact, bool isStacked)
    {
        if (isStacked)
        {
            return new Thickness(0, 10, 0, 0);
        }
        return isCompact ? new Thickness(12, 0, 12, 0) : new Thickness(32, 0, 32, 0);
    }

    public Thickness GetBarPadding(bool isStacked)
        => isStacked ? new Thickness(12, 10, 12, 10) : new Thickness(20, 12, 20, 12);

    public MiniPlaybackBar()
    {
        this.InitializeComponent();
        SeekArea.RegisterPropertyChangedCallback(IsHitTestVisibleProperty, (s, dp) =>
        {
            if (!SeekArea.IsHitTestVisible)
            {
                EndSeekHover();
            }
        });
        Loaded += (_, _) => AttachLayout(Layout);
        Unloaded += (_, _) => DetachLayout(Layout);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        MiniPlaybackBar bar = (MiniPlaybackBar)d;
        bar.DetachLayout((ResponsiveLayoutManager?)e.OldValue);
        if (bar.IsLoaded)
        {
            bar.AttachLayout((ResponsiveLayoutManager?)e.NewValue);
        }
    }

    private void AttachLayout(ResponsiveLayoutManager? layout)
    {
        if (layout is null)
        {
            return;
        }
        layout.PropertyChanged -= OnLayoutPropertyChanged;
        layout.PropertyChanged += OnLayoutPropertyChanged;
        ApplySeekArrangement(layout.IsKioskBarStacked);
    }

    private void DetachLayout(ResponsiveLayoutManager? layout)
    {
        if (layout is not null)
        {
            layout.PropertyChanged -= OnLayoutPropertyChanged;
        }
    }

    private void OnLayoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResponsiveLayoutManager.IsKioskBarStacked) && sender is ResponsiveLayoutManager layout)
        {
            ApplySeekArrangement(layout.IsKioskBarStacked);
        }
    }

    // Moves the seek bar and collapses its column together, so the title takes the freed width.
    private void ApplySeekArrangement(bool isStacked)
    {
        Grid.SetRow(SeekArea, isStacked ? 1 : 0);
        Grid.SetColumn(SeekArea, isStacked ? 0 : 2);
        Grid.SetColumnSpan(SeekArea, isStacked ? 6 : 1);
        ProgressColumn.Width = isStacked ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
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
