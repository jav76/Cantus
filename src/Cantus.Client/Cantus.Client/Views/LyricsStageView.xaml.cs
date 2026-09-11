using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;

namespace Cantus.Client.Views;

public sealed partial class LyricsStageView : UserControl
{
    private const int LINE_ACTIVATION_ANIMATION_MS = 250;

    private readonly DispatcherTimer _autoResumeTimer;
    private readonly DispatcherTimer _programmaticScrollResetTimer;
    private bool _isProgrammaticScroll;
    private int _lastAnimatedIndex = -1;
    private Storyboard? _activationStoryboard;
    private Storyboard? _deactivationStoryboard;

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
                oldVm.LyricsReloaded -= view.OnLyricsReloaded;
            }

            if (e.NewValue is LyricsViewModel newVm)
            {
                newVm.AutoScrollResumed += view.OnAutoScrollResumed;
                newVm.LyricsReloaded += view.OnLyricsReloaded;
            }
        }
    }

    private void OnLyricsReloaded()
    {
        // A new track's lyrics replaced the collection: jump back to the top
        // instantly (animating from the previous song's offset would sweep
        // through unrelated lines). The programmatic-scroll guard keeps the
        // jump from being mistaken for a manual scroll.
        try
        {
            _isProgrammaticScroll = true;
            _programmaticScrollResetTimer.Stop();
            _programmaticScrollResetTimer.Start();
            LyricsScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
            StaticLyricsScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch
        {
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

        // Runs before the autoscroll guards: the activation animation should play
        // even while the user has scrolling paused.
        AnimateLineActivation(idx);

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

    /// <summary>
    /// Masks the instant active-line font-size snap with a short scale ease:
    /// the newly active line grows from its inactive size ratio to full size,
    /// and the previously active line settles down from its active ratio.
    /// Scale transforms do not reflow layout, and Storyboards do not raise
    /// ScrollViewer.ViewChanged, so the manual-scroll detector is unaffected.
    /// </summary>
    private void AnimateLineActivation(int newIdx)
    {
        if (ViewModel is null || newIdx == _lastAnimatedIndex)
        {
            return;
        }

        int previousIdx = _lastAnimatedIndex;
        _lastAnimatedIndex = newIdx;

        try
        {
            double activeSize = ViewModel.Layout.ActiveLyricsFontSize;
            double inactiveSize = ViewModel.Layout.InactiveLyricsFontSize;
            if (activeSize <= 0 || inactiveSize <= 0)
            {
                return;
            }

            _activationStoryboard?.Stop();
            _activationStoryboard = StartScaleAnimation(newIdx, inactiveSize / activeSize);

            if (previousIdx >= 0 && previousIdx < ViewModel.LyricLines.Count)
            {
                _deactivationStoryboard?.Stop();
                _deactivationStoryboard = StartScaleAnimation(previousIdx, activeSize / inactiveSize);
            }
        }
        catch
        {
        }
    }

    private Storyboard? StartScaleAnimation(int idx, double fromScale)
    {
        if (LyricsItemsControl.ContainerFromIndex(idx) is not FrameworkElement element)
        {
            return null;
        }

        if (element.RenderTransform is not ScaleTransform)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new ScaleTransform();
        }

        Storyboard storyboard = new();
        foreach (string property in new[] { "ScaleX", "ScaleY" })
        {
            DoubleAnimation animation = new()
            {
                From = fromScale,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(LINE_ACTIVATION_ANIMATION_MS)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animation, element.RenderTransform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        storyboard.Begin();
        return storyboard;
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

    private void OnToggleKaraokeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleKaraokeMode();
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
        // Stretch on every breakpoint: combined with LyricsMaxWidth it yields a
        // constant, self-centering width. Center would size the list to the widest
        // currently-rendered line, which changes with the active line's larger font
        // and makes the lyrics box visibly resize as the song progresses.
        return HorizontalAlignment.Stretch;
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
