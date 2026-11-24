using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

public sealed partial class LocalSessionDialog : ContentDialog
{
    public LocalSessionSetupViewModel Setup { get; }

    public ThemeManager Theme { get; }

    public LocalSessionDialog(LocalSessionSetupViewModel setup, ThemeManager theme)
    {
        Setup = setup;
        Theme = theme;
        this.InitializeComponent();
    }

    // The dialog stays open on a refusal so the message and join code field can be acted on.
    private async void OnConnectClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (await Setup.ConnectAsync())
        {
            Hide();
        }
    }
}
