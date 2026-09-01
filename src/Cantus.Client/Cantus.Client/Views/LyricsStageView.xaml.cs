using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Cantus.Client.Views;

public sealed partial class LyricsStageView : UserControl
{
    private readonly DispatcherTimer _autoResumeTimer;
    private readonly DispatcherTimer _programmaticScrollResetTimer;
    private bool _isProgrammaticScroll;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(LyricsStageView),
            new PropertyMetadata(null, OnViewModelPropertyChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public LyricsStageView()
    {
        InitializeComponent();

        _autoResumeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _autoResumeTimer.Tick += OnAutoResumeTimerTick;

        _programmaticScrollResetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _programmaticScrollResetTimer.Tick += OnProgrammaticScrollResetTimerTick;

        this.Loaded += (s, e) =>
        {
            UpdateContainerPadding();
            if (ViewModel is not null && ViewModel.ActiveLineIndex >= 0)
            {
                ScrollToActiveLine(ViewModel.ActiveLineIndex, force: true);
            }
        };
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LyricsStageView view)
        {
            if (e.OldValue is LyricsViewModel oldVm)
            {
                oldVm.AutoScrollResumed -= view.OnAutoScrollResumed;
            }

            if (e.NewValue is LyricsViewModel newVm)
            {
                newVm.AutoScrollResumed += view.OnAutoScrollResumed;
            }
        }
    }

    private void OnAutoScrollResumed()
    {
        _autoResumeTimer.Stop();
        if (ViewModel is not null && ViewModel.ActiveLineIndex >= 0)
        {
            ScrollToActiveLine(ViewModel.ActiveLineIndex, force: true);
        }
    }

    private void OnAutoResumeTimerTick(object? sender, object e)
    {
        _autoResumeTimer.Stop();
        ViewModel?.ResumeAutoScroll();
    }

    private void OnProgrammaticScrollResetTimerTick(object? sender, object e)
    {
        _programmaticScrollResetTimer.Stop();
        _isProgrammaticScroll = false;
    }

    public void ScrollToActiveLine(int idx, bool force = false)
    {
        if (ViewModel is null || idx < 0 || idx >= ViewModel.LyricLines.Count) return;
        if (!ViewModel.IsAutoScrollEnabled && !force) return;
        if (ViewModel.IsUserScrollingPaused && !force) return;

        DependencyObject? container = LyricsItemsControl.ContainerFromIndex(idx);
        if (container is FrameworkElement element && element.ActualHeight > 0)
        {
            try
            {
                GeneralTransform transform = element.TransformToVisual(LyricsItemsControl);
                Point pt = transform.TransformPoint(new Point(0, 0));
                double itemCenterY = pt.Y + (element.ActualHeight / 2.0);

                double viewportHeight = LyricsScrollViewer.ActualHeight > 0
                    ? LyricsScrollViewer.ActualHeight
                    : LyricsPresentationContainer.ActualHeight;

                if (viewportHeight <= 0) viewportHeight = 400.0;

                double targetOffset = Math.Max(0, itemCenterY - (viewportHeight * 0.40));

                _isProgrammaticScroll = true;
                _programmaticScrollResetTimer.Stop();
                _programmaticScrollResetTimer.Start();

                LyricsScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
            }
            catch
            {
            }
        }
        else
        {
            _ = DispatcherQueue?.TryEnqueue(() =>
            {
                if (ViewModel is not null && idx == ViewModel.ActiveLineIndex)
                {
                    ScrollToActiveLine(idx, force);
                }
            });
        }
    }

    private void OnPresentationContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContainerPadding();
    }

    private void UpdateContainerPadding()
    {
        double viewportHeight = LyricsPresentationContainer.ActualHeight > 0
            ? LyricsPresentationContainer.ActualHeight
            : LyricsScrollViewer.ActualHeight;

        if (viewportHeight > 0)
        {
            double topPadding = Math.Max(0, viewportHeight * 0.40);
            double bottomPadding = Math.Max(0, viewportHeight * 0.60);
            LyricsItemsControl.Padding = new Thickness(16, topPadding, 16, bottomPadding);
        }
    }

    private void OnScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        OnUserManualScroll();
    }

    private void OnScrollViewerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        OnUserManualScroll();
    }

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_isProgrammaticScroll && e.IsIntermediate)
        {
            OnUserManualScroll();
        }
    }

    private void OnUserManualScroll()
    {
        if (ViewModel is not null && ViewModel.IsAutoScrollEnabled && !_isProgrammaticScroll)
        {
            ViewModel.SetUserScrollingPaused(true);
            _autoResumeTimer.Stop();
            _autoResumeTimer.Start();
        }
    }

    private void OnToggleAutoScrollClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        ViewModel.ToggleAutoScroll();
        if (ViewModel.IsAutoScrollEnabled)
        {
            _autoResumeTimer.Stop();
            ScrollToActiveLine(ViewModel.ActiveLineIndex, force: true);
        }
    }

    private void OnResumeAutoScrollClicked(object sender, RoutedEventArgs e)
    {
        _autoResumeTimer.Stop();
        ViewModel?.ResumeAutoScroll();
    }

    private void OnToggleStaticModeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleStaticLyricsMode();
    }

    public SolidColorBrush GetAutoScrollButtonBackground(bool? isAutoScrollEnabled = null)
    {
        return isAutoScrollEnabled.GetValueOrDefault()
            ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
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
}
