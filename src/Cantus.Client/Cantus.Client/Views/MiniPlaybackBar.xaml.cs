using System;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class MiniPlaybackBar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(MiniPlaybackBar),
            new PropertyMetadata(null));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MiniPlaybackBar()
    {
        this.InitializeComponent();
    }

    private void OnToggleKioskClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleKioskMode();
    }
}
