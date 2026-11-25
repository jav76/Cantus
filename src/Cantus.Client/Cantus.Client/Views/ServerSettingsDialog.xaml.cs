using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Cantus.Client.Views;

public sealed partial class ServerSettingsDialog : ContentDialog
{
    public ServerSetupViewModel Setup { get; }

    public ThemeManager Theme { get; }

    /// <summary>What saving did; <see cref="ServerSetupResult.NotSaved"/> when the dialog was cancelled.</summary>
    public ServerSetupResult Result { get; private set; } = ServerSetupResult.NotSaved;

    public ServerSettingsDialog(ServerSetupViewModel setup, ThemeManager theme)
    {
        Setup = setup;
        Theme = theme;
        this.InitializeComponent();
        DialogLightDismiss.Enable(this);
    }

    public Brush GetStatusBrush(ServerCheckState state) => ServerSettingsEntry.GetStatusBrush(state, Theme);

    // The dialog stays open for an address no server answers at, so the warning can be read and confirmed.
    private async void OnSaveClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ServerSetupResult result = await Setup.SaveAsync();
        if (result != ServerSetupResult.NotSaved)
        {
            Result = result;
            Hide();
        }
    }

    private void OnUseThisComputerClicked(object sender, RoutedEventArgs e) => Setup.UseThisComputer();
}
