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
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty TimingProperty =
        DependencyProperty.Register(
            nameof(Timing),
            typeof(PlaybackTiming),
            typeof(MobileSettingsView),
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
            typeof(MobileSettingsView),
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
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public DisplayPairingViewModel? Pairing
    {
        get => (DisplayPairingViewModel?)GetValue(PairingProperty);
        private set => SetValue(PairingProperty, value);
    }

    public static readonly DependencyProperty DisplaysProperty =
        DependencyProperty.Register(
            nameof(Displays),
            typeof(DisplaysViewModel),
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public DisplaysViewModel? Displays
    {
        get => (DisplaysViewModel?)GetValue(DisplaysProperty);
        private set => SetValue(DisplaysProperty, value);
    }

    public static readonly DependencyProperty LyricsPickerProperty =
        DependencyProperty.Register(
            nameof(LyricsPicker),
            typeof(LyricsPickerViewModel),
            typeof(MobileSettingsView),
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
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    public static readonly DependencyProperty EffectsProperty =
        DependencyProperty.Register(
            nameof(Effects),
            typeof(VisualEffectsViewModel),
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public VisualEffectsViewModel? Effects
    {
        get => (VisualEffectsViewModel?)GetValue(EffectsProperty);
        private set => SetValue(EffectsProperty, value);
    }

    public static readonly DependencyProperty VuMeterProperty =
        DependencyProperty.Register(
            nameof(VuMeter),
            typeof(VuMeterViewModel),
            typeof(MobileSettingsView),
            new PropertyMetadata(null));

    public VuMeterViewModel? VuMeter
    {
        get => (VuMeterViewModel?)GetValue(VuMeterProperty);
        private set => SetValue(VuMeterProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        MobileSettingsView settings = (MobileSettingsView)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        settings.Timing = viewModel?.Timing;
        settings.Session = viewModel?.Session;
        settings.Pairing = viewModel?.Pairing;
        settings.Displays = viewModel?.Session.Displays;
        settings.LyricsPicker = viewModel?.LyricsPicker;
        settings.Theme = viewModel?.Theme;
        settings.Effects = viewModel?.Effects;
        settings.VuMeter = viewModel?.VuMeter;
    }

    public MobileSettingsView()
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

    private void OnLatencyMinusClicked(object sender, RoutedEventArgs e)
    {
        Timing?.AdjustLatencyCompensation(-50);
    }

    private void OnLatencyPlusClicked(object sender, RoutedEventArgs e)
    {
        Timing?.AdjustLatencyCompensation(50);
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

    /// <summary>The Displays card shows only for a signed-in owner, never on a Display.</summary>
    public Visibility GetDisplaysCardVisibility(Visibility ownerControls, Visibility displaysSection) =>
        BothVisible(ownerControls, displaysSection);

    /// <summary>The "Wrong lyrics?" card is owner-only, so it never shows on a Display.</summary>
    public Visibility GetLyricsPickerVisibility(Visibility ownerControls, Visibility pickerAction) =>
        BothVisible(ownerControls, pickerAction);

    private static Visibility BothVisible(Visibility first, Visibility second) =>
        first == Visibility.Visible && second == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string GetRttText(long? rtt = null) => $"{rtt.GetValueOrDefault()}ms";
    public string GetSkewText(long? skew = null)
    {
        long s = skew.GetValueOrDefault();
        return $"{(s >= 0 ? "+" : "")}{s}ms";
    }
}
