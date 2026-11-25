using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Cantus.Client.Views;

/// <summary>
/// The desktop app's first-run prompt for the server address, shown before <see cref="MainPage"/>
/// so the app connects once, to the chosen server.
/// </summary>
public sealed partial class ServerSetupPage : Page
{
    public ServerSetupViewModel Setup { get; }

    public ThemeManager Theme { get; } = ThemeManager.Instance;

    public ServerSetupPage()
    {
        Setup = ServerSettingsEntry.CreateViewModel();
        this.InitializeComponent();
        this.Loaded += (_, _) => AddressBox.Focus(FocusState.Programmatic);
    }

    public Brush GetStatusBrush(ServerCheckState state) => ServerSettingsEntry.GetStatusBrush(state, Theme);

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        await SaveAsync();
    }

    private async void OnAddressKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SaveAsync();
        }
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        Setup.Skip();
        OpenMainPage();
    }

    private async System.Threading.Tasks.Task SaveAsync()
    {
        if (await Setup.SaveAsync() != ServerSetupResult.NotSaved)
        {
            OpenMainPage();
        }
    }

    private void OpenMainPage()
    {
        ServerSelection selection = ClientStartupOptions.ResolveServer(new ServerSettingsStore(new LocalSettingsStore()));
        LocalServerManager.ForThisApp.BeginStartup(selection.BaseUrl);
        Frame.Navigate(typeof(MainPage));
        Frame.BackStack.Clear();
    }
}
