using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cantus.Client.Views;

public sealed partial class LyricsStageView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(LyricsStageView),
            new PropertyMetadata(null));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public LyricsStageView()
    {
        InitializeComponent();
    }

    public void ScrollToActiveLine(int idx)
    {
        if (ViewModel is not null && idx >= 0 && idx < ViewModel.LyricLines.Count)
        {
            LyricLineViewModel activeItem = ViewModel.LyricLines[idx];
            LyricsListView.ScrollIntoView(activeItem);
        }
    }

    public Visibility GetEmptyStateVisibility(bool? hasLyrics = null)
        => hasLyrics.GetValueOrDefault() ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GetLyricsVisibility(bool? hasLyrics = null)
        => hasLyrics.GetValueOrDefault() ? Visibility.Visible : Visibility.Collapsed;

    public Thickness GetStagePadding(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => new Thickness(16, 12, 16, 12),
            LayoutBreakpoint.Medium => new Thickness(24, 18, 24, 18),
            LayoutBreakpoint.FullscreenTv => new Thickness(48, 24, 48, 24),
            _ => new Thickness(32)
        };
    }

    public double GetEmptyIconSize(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => 40.0,
            LayoutBreakpoint.Medium => 48.0,
            _ => 56.0
        };
    }

    public double GetEmptyTitleSize(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => 16.0,
            LayoutBreakpoint.Medium => 18.0,
            _ => 20.0
        };
    }

    public double GetEmptySubtitleSize(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.Small => 12.0,
            LayoutBreakpoint.Medium => 14.0,
            _ => 16.0
        };
    }

    public HorizontalAlignment GetListHorizontalAlignment(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp switch
        {
            LayoutBreakpoint.FullscreenTv => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Stretch
        };
    }

    public static TextAlignment GetLineTextAlignment(bool? isActive = null)
    {
        LayoutBreakpoint bp = ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.FullscreenTv ? TextAlignment.Center : TextAlignment.Left;
    }

    public static SolidColorBrush GetLineColor(bool? isActive = null, bool? isPast = null)
    {
        ThemeManager tm = ThemeManager.Instance;
        if (isActive.GetValueOrDefault()) return tm.ActiveLyricBrush;
        if (isPast.GetValueOrDefault()) return tm.PastLyricBrush;
        return tm.UpcomingLyricBrush;
    }

    private void OnToggleStaticModeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleStaticLyricsMode();
    }
}
