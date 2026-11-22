using System;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Cantus.Client.Views;

public sealed partial class SpotifySetupDialog : ContentDialog
{
    private static readonly ILogger<SpotifySetupDialog> _logger = ClientLoggingManager.CreateLogger<SpotifySetupDialog>();

    public SpotifySetupViewModel Setup { get; }

    public ThemeManager Theme { get; }

    public SpotifySetupDialog(SpotifySetupViewModel setup, ThemeManager theme)
    {
        Setup = setup;
        Theme = theme;
        this.InitializeComponent();
    }

    // RadioButton.IsChecked is bool?, so the choice is written back from Checked
    // rather than through a TwoWay binding.
    private void OnServerAppChecked(object sender, RoutedEventArgs e) => Setup.UseOwnApp = false;

    private void OnOwnAppChecked(object sender, RoutedEventArgs e) => Setup.UseOwnApp = true;

    private void OnForgetClicked(object sender, RoutedEventArgs e) => Setup.ForgetSavedClientId();

    private void OnCopyRedirectUriClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            DataPackage package = new();
            package.SetText(Setup.RedirectUri);
            Clipboard.SetContent(package);
            CopyRedirectButton.Content = "Copied";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copying the Spotify redirect URI failed");
        }
    }
}
