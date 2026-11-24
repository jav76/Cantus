using System;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Cantus.Client.Views;

public sealed partial class SongLookupDialog : ContentDialog
{
    private static readonly ILogger<SongLookupDialog> _logger = ClientLoggingManager.CreateLogger<SongLookupDialog>();

    public SongLookupViewModel Lookup { get; }

    public ThemeManager Theme { get; }

    public SongLookupDialog(LyricsViewModel viewModel)
    {
        Lookup = viewModel.Lookup;
        Theme = viewModel.Theme;
        this.InitializeComponent();
        DialogLightDismiss.Enable(this);
        Opened += (_, _) => FragmentBox.Focus(FocusState.Programmatic);
        Closed += (_, _) => Lookup.Cancel();
    }

    public Visibility GetEmptyHintVisibility(SongLookupState state)
    {
        return state == SongLookupState.Idle && Lookup.Guesses.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OnFragmentKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        // The TwoWay binding may not have pushed the last keystroke yet when Enter arrives.
        if (sender is TextBox box)
        {
            Lookup.Fragment = box.Text;
        }

        await RunSafelyAsync(Lookup.SearchAsync, "search");
    }

    private async void OnSearchClicked(object sender, RoutedEventArgs e)
    {
        Lookup.Fragment = FragmentBox.Text;
        await RunSafelyAsync(Lookup.SearchAsync, "search");
    }

    private async void OnSearchOnlineClicked(object sender, RoutedEventArgs e)
    {
        await RunSafelyAsync(Lookup.SearchOnlineAsync, "online search");
    }

    private async void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SongGuessViewModel guess })
        {
            await RunSafelyAsync(guess.PlayAsync, "play");
        }
    }

    private async void OnQueueClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SongGuessViewModel guess })
        {
            await RunSafelyAsync(guess.QueueAsync, "queue");
        }
    }

    private static async Task RunSafelyAsync(Func<Task> action, string actionName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Song lookup {Action} failed", actionName);
        }
    }
}
