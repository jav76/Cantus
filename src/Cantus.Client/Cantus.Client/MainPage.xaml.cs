using System;
using System.Net.Http;
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

    public ThemeManager Theme => ViewModel.Theme;

    public ResponsiveLayoutManager Layout => ViewModel.Layout;

    public DisplayPairingViewModel Pairing => ViewModel.Pairing;

    public DisplaysViewModel Displays => ViewModel.Session.Displays;

    public MainPage()
    {
        SignalRPlaybackClient client = new(ClientStartupOptions.ServerUrl);
        LyricsPreferences preferences = new(new LocalSettingsStore());
        SongLookupClient lookupClient = new(client, new HttpClient());
        ViewModel = new LyricsViewModel(
            client,
            ThemeManager.Instance,
            ResponsiveLayoutManager.Instance,
            preferences,
            lookupClient: lookupClient);
        this.InitializeComponent();
        OpenPairLink();

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

        DispatcherTimer visibilityChecker = new() { Interval = TimeSpan.FromSeconds(2) };
        bool lastVisibility = true;
        visibilityChecker.Tick += async (s, e) =>
        {
            bool currentVis = WasmInterop.IsDocumentVisible();
            if (currentVis != lastVisibility)
            {
                lastVisibility = currentVis;
                await ViewModel.ReportVisibilityAsync(currentVis);
                if (currentVis)
                {
                    await ViewModel.RefreshPlaybackAsync();
                }
            }
        };
        visibilityChecker.Start();

        this.PointerPressed += async (s, e) =>
        {
            if (!lastVisibility)
            {
                lastVisibility = true;
                await ViewModel.ReportVisibilityAsync(true);
                await ViewModel.RefreshPlaybackAsync();
            }
        };
    }

    /// <summary>
    /// A <c>?pair=</c> link from a Display's QR opens the approval prompt. The parameter is
    /// removed from the address so a reload does not ask again.
    /// </summary>
    private void OpenPairLink()
    {
        string search = WasmInterop.GetLocationSearch();
        if (string.IsNullOrEmpty(search) || Pairing.IsDisplay)
        {
            return;
        }

        if (Displays.OpenApprovalFromLink(search))
        {
            WasmInterop.RemoveQueryParameter(DisplayPairingViewModel.PAIR_QUERY_PARAMETER);
        }
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
        // While the Display prompt is open, Escape closes it even when focus is outside it.
        if (Displays.IsPromptOpen)
        {
            if (e.Key == VirtualKey.Escape)
            {
                Displays.CancelPrompt();
                e.Handled = true;
            }
            return;
        }

        // Letters typed into a field (including the Find a song box inside a dialog) are not shortcuts.
        if (e.OriginalSource is TextBox
            || (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is TextBox))
        {
            return;
        }

        if (e.Key == VirtualKey.F11 || e.Key == VirtualKey.K)
        {
            ViewModel.Layout.ToggleKioskMode();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.T)
        {
            ViewModel.Theme.CycleNextTheme();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.M && ViewModel.Layout.IsSmall)
        {
            ViewModel.Layout.CycleMobileView();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && ViewModel.Layout.IsKioskMode)
        {
            ViewModel.Layout.IsKioskMode = false;
            e.Handled = true;
        }
        else if ((int)e.Key == 219) // [ (OEM4): reduce compensation, lyrics render earlier
        {
            ViewModel.Timing.AdjustLatencyCompensation(-50);
            e.Handled = true;
        }
        else if ((int)e.Key == 221) // ] (OEM6): increase compensation, lyrics render later
        {
            ViewModel.Timing.AdjustLatencyCompensation(50);
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
