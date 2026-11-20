using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

/// <summary>The owner's Displays list with rename and remove, shown in settings while signed in.</summary>
public sealed partial class DisplaysCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(DisplaysCard),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty DisplaysProperty =
        DependencyProperty.Register(
            nameof(Displays),
            typeof(DisplaysViewModel),
            typeof(DisplaysCard),
            new PropertyMetadata(null));

    public DisplaysViewModel? Displays
    {
        get => (DisplaysViewModel?)GetValue(DisplaysProperty);
        private set => SetValue(DisplaysProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(DisplaysCard),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    public string EmptyMessage => DisplaysViewModel.EMPTY_MESSAGE;

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DisplaysCard card = (DisplaysCard)d;
        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        card.Displays = viewModel?.Session.Displays;
        card.Theme = viewModel?.Theme;
    }

    public DisplaysCard()
    {
        InitializeComponent();
    }

    private void OnApproveClicked(object sender, RoutedEventArgs e)
    {
        Displays?.OpenApproval();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (Displays is not null)
        {
            await Displays.RefreshAsync();
        }
    }

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DisplayItemViewModel display })
        {
            Displays?.StartRename(display);
        }
    }

    private void OnRevokeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DisplayItemViewModel display })
        {
            Displays?.StartRevoke(display);
        }
    }
}
