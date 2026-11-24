using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

/// <summary>The "Players on this computer" section of the side panel and the phone settings tab.</summary>
public sealed partial class LocalPlayersCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(LocalPlayersCard),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty PlayersProperty =
        DependencyProperty.Register(
            nameof(Players),
            typeof(LocalPlayersViewModel),
            typeof(LocalPlayersCard),
            new PropertyMetadata(null));

    public LocalPlayersViewModel? Players
    {
        get => (LocalPlayersViewModel?)GetValue(PlayersProperty);
        private set => SetValue(PlayersProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(LocalPlayersCard),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        LocalPlayersCard card = (LocalPlayersCard)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        card.Players = viewModel?.Session.LocalPlayers;
        card.Theme = viewModel?.Theme;
    }

    public LocalPlayersCard()
    {
        InitializeComponent();
    }
}
