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
        if (ViewModel != null) await ViewModel.NudgeOffsetAsync(-500);
    }

    private async void OnNudgeMinus100Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.NudgeOffsetAsync(-100);
    }

    private async void OnResetOffsetClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.ResetOffsetAsync();
    }

    private async void OnNudgePlus100Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.NudgeOffsetAsync(100);
    }

    private async void OnNudgePlus500Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.NudgeOffsetAsync(500);
    }

    private async void OnSessionItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorizedSessionPayload session && ViewModel != null)
        {
            await ViewModel.SubscribeToUserAsync(session.Id);
        }
    }

    public Visibility GetStandardCardVisibility(LayoutBreakpoint? breakpoint = null)
    {
        var bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp != LayoutBreakpoint.Small ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetMobileStripVisibility(LayoutBreakpoint? breakpoint = null)
    {
        var bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.Small ? Visibility.Visible : Visibility.Collapsed;
    }

    public Thickness GetCardPadding(LayoutBreakpoint? breakpoint = null)
    {
        var bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => new Thickness(12, 10, 12, 10),
            LayoutBreakpoint.Medium => new Thickness(16),
            _ => new Thickness(24)
        };
    }

    public double GetCardSpacing(LayoutBreakpoint? breakpoint = null)
    {
        var bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Medium => 12.0,
            _ => 18.0
        };
    }

    public double GetAlbumIconSize(LayoutBreakpoint? breakpoint = null)
    {
        var bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Medium => 48.0,
            _ => 64.0
        };
    }

    public double GetTitleFontSize(LayoutBreakpoint? breakpoint = null)
    {
        var bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Medium => 18.0,
            _ => 22.0
        };
    }

    public double GetArtistFontSize(LayoutBreakpoint? breakpoint = null)
    {
        var bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
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
