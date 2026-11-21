using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cantus.Client.Views;

public sealed partial class MobileTabBar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(MobileTabBar),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(MobileTabBar),
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
            typeof(MobileTabBar),
            new PropertyMetadata(null));

    public ResponsiveLayoutManager? Layout
    {
        get => (ResponsiveLayoutManager?)GetValue(LayoutProperty);
        private set => SetValue(LayoutProperty, value);
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        MobileTabBar tabBar = (MobileTabBar)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        tabBar.Theme = viewModel?.Theme;
        tabBar.Layout = viewModel?.Layout;
    }

    public MobileTabBar()
    {
        this.InitializeComponent();
    }

    private void OnLyricsTabClicked(object sender, RoutedEventArgs e)
    {
        SetMobileView(MobileViewMode.Lyrics);
    }

    private void OnNowPlayingTabClicked(object sender, RoutedEventArgs e)
    {
        SetMobileView(MobileViewMode.NowPlaying);
    }

    private void OnSyncTabClicked(object sender, RoutedEventArgs e)
    {
        SetMobileView(MobileViewMode.SyncAndSettings);
    }

    private void SetMobileView(MobileViewMode mode)
    {
        if (Layout is not null)
        {
            Layout.MobileView = mode;
        }
    }

    public Orientation GetTabContentOrientation(bool isNarrowPhone)
        => isNarrowPhone ? Orientation.Vertical : Orientation.Horizontal;

    public double GetTabContentSpacing(bool isNarrowPhone) => isNarrowPhone ? 2.0 : 6.0;

    public Brush GetTabBackground(MobileViewMode? activeMode = null, MobileViewMode currentTab = MobileViewMode.Lyrics)
    {
        MobileViewMode active = activeMode ?? ResponsiveLayoutManager.Instance.MobileView;
        if (active == currentTab)
        {
            return ThemeManager.Instance.PrimaryAccentBrush;
        }
        return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    public Brush GetTabForeground(MobileViewMode? activeMode = null, MobileViewMode currentTab = MobileViewMode.Lyrics)
    {
        MobileViewMode active = activeMode ?? ResponsiveLayoutManager.Instance.MobileView;
        if (active == currentTab)
        {
            return ThemeManager.Instance.OnPrimaryAccentBrush;
        }
        return ThemeManager.Instance.TextMutedBrush;
    }
}
