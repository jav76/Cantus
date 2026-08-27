using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Cantus.Client.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Cantus.Client;

public sealed partial class MainPage : Page
{
    public LyricsViewModel ViewModel { get; }

    public MainPage()
    {
        SignalRPlaybackClient client = new();
        ViewModel = new LyricsViewModel(client, ThemeManager.Instance, ResponsiveLayoutManager.Instance);
        this.InitializeComponent();

        this.Loaded += async (s, e) =>
        {
            if (this.ActualWidth > 0 && this.ActualHeight > 0)
            {
                ViewModel.Layout.UpdateDimensions(this.ActualWidth, this.ActualHeight);
            }
            await ViewModel.InitializeAsync();
        };

        ViewModel.ActiveLineChanged += OnActiveLineChanged;
        this.KeyDown += OnPageKeyDown;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            ViewModel.Layout.UpdateDimensions(e.NewSize.Width, e.NewSize.Height);
        }
    }

    private void OnActiveLineChanged(int idx)
    {
        if (idx < 0 || idx >= ViewModel.LyricLines.Count) return;

        if (ViewModel.Layout.IsFullscreenTv)
        {
            KioskLyricsStage?.ScrollToActiveLine(idx);
        }
        else if (ViewModel.Layout.IsSmall)
        {
            MobileLyricsStage?.ScrollToActiveLine(idx);
        }
        else
        {
            StandardLyricsStage?.ScrollToActiveLine(idx);
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F11 || e.Key == VirtualKey.K)
        {
            ViewModel.ToggleKioskMode();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.T)
        {
            ViewModel.Theme.CycleNextTheme();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.M && ViewModel.Layout.IsSmall)
        {
            ViewModel.CycleMobileView();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && ViewModel.IsKioskMode)
        {
            ViewModel.IsKioskMode = false;
            e.Handled = true;
        }
    }

    public Visibility GetStandardDesktopViewVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return (bp == LayoutBreakpoint.Large || bp == LayoutBreakpoint.Medium)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public Visibility GetMobileViewVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.Small ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetKioskViewVisibility(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.FullscreenTv ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetMobileLyricsVisibility(MobileViewMode? viewMode = null)
    {
        MobileViewMode vm = viewMode ?? ResponsiveLayoutManager.Instance.MobileView;
        return vm == MobileViewMode.Lyrics ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetMobileNowPlayingVisibility(MobileViewMode? viewMode = null)
    {
        MobileViewMode vm = viewMode ?? ResponsiveLayoutManager.Instance.MobileView;
        return vm == MobileViewMode.NowPlaying ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility GetMobileSettingsVisibility(MobileViewMode? viewMode = null)
    {
        MobileViewMode vm = viewMode ?? ResponsiveLayoutManager.Instance.MobileView;
        return vm == MobileViewMode.SyncAndSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    public double GetColumnSpacing(LayoutBreakpoint? breakpoint = null)
    {
        LayoutBreakpoint bp = breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint;
        return bp == LayoutBreakpoint.Medium ? 16.0 : 24.0;
    }
}
