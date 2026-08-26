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
            new PropertyMetadata(null));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MobileTabBar()
    {
        this.InitializeComponent();
    }

    private void OnLyricsTabClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetMobileView(MobileViewMode.Lyrics);
    }

    private void OnNowPlayingTabClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetMobileView(MobileViewMode.NowPlaying);
    }

    private void OnSyncTabClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetMobileView(MobileViewMode.SyncAndSettings);
    }

    public Brush GetTabBackground(MobileViewMode? activeMode = null, MobileViewMode currentTab = MobileViewMode.Lyrics)
    {
        var active = activeMode ?? ResponsiveLayoutManager.Instance.MobileView;
        if (active == currentTab)
        {
            return ThemeManager.Instance.PrimaryAccentBrush;
        }
        return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    public Brush GetTabForeground(MobileViewMode? activeMode = null, MobileViewMode currentTab = MobileViewMode.Lyrics)
    {
        var active = activeMode ?? ResponsiveLayoutManager.Instance.MobileView;
        if (active == currentTab)
        {
            return new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        }
        return ThemeManager.Instance.TextMutedBrush;
    }
}
