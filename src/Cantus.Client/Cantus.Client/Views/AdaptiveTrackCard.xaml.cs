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

    public static readonly DependencyProperty LocalPlayersProperty =
        DependencyProperty.Register(
            nameof(LocalPlayers),
            typeof(LocalPlayersViewModel),
            typeof(AdaptiveTrackCard),
            new PropertyMetadata(null));

    public LocalPlayersViewModel? LocalPlayers
    {
        get => (LocalPlayersViewModel?)GetValue(LocalPlayersProperty);
        private set => SetValue(LocalPlayersProperty, value);
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
        card.LocalPlayers = viewModel?.Session.LocalPlayers;
        card.Theme = viewModel?.Theme;
        card.Layout = viewModel?.Layout;
    }

    public AdaptiveTrackCard()
    {
        InitializeComponent();
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

    private async void OnUseLocalPlayersClicked(object sender, RoutedEventArgs e)
    {
        await LocalSessionSignIn.StartAsync(Session, XamlRoot);
    }

    private async void OnAccountClicked(object sender, RoutedEventArgs e)
    {
        await AccountMenu.OpenAsync(Session, (FrameworkElement)sender);
    }

    private async void OnPairAsDisplayClicked(object sender, RoutedEventArgs e)
    {
        if (Pairing is not null)
        {
            await Pairing.StartPairingAsync();
        }
    }

    /// <summary>The "Wrong lyrics?" button is owner-only, so it never shows on a Display.</summary>
    public Visibility GetLyricsPickerVisibility(Visibility ownerControls, Visibility pickerAction) =>
        ownerControls == Visibility.Visible && pickerAction == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        return NowPlayingPanel.SectionSpacingFor(bp);
    }

    public static Visibility GetPlayingVisibility(bool isPlaying)
        => isPlaying ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility GetNoSessionsVisibility(int count)
        => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility GetHasSessionsVisibility(int count)
        => count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
