using System;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

/// <summary>
/// Starts a Spotify sign-in from any Connect Spotify button: shows the setup screen when the
/// server lets the user choose their own Spotify app, then opens the sign-in page (same tab on
/// WASM, the system browser elsewhere).
/// </summary>
internal static class SpotifySignIn
{
    private const string DEFAULT_SERVER_URL = "http://127.0.0.1:5000";

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger(nameof(SpotifySignIn));

    /// <param name="beforeLeave">Runs once the sign-in is about to start, after any setup screen.</param>
    public static async Task StartAsync(SessionViewModel? session, XamlRoot? xamlRoot, Action? beforeLeave = null)
    {
        try
        {
            string? loginPath = SessionViewModel.SPOTIFY_LOGIN_PATH;
            if (session is not null)
            {
                SpotifySetupViewModel setup = session.SpotifySetup;
                await setup.LoadAsync();
                if (setup.NeedsDialog && !await ShowSetupAsync(setup, xamlRoot))
                {
                    return;
                }

                loginPath = setup.PrepareLoginPath();
                if (loginPath is null)
                {
                    return;
                }
            }

            beforeLeave?.Invoke();
            await LaunchAsync(session?.ServerBaseUrl, loginPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Spotify");
        }
    }

    private static async Task<bool> ShowSetupAsync(SpotifySetupViewModel setup, XamlRoot? xamlRoot)
    {
        SpotifySetupDialog dialog = new(setup, ThemeManager.Instance);
        if (xamlRoot is not null)
        {
            dialog.XamlRoot = xamlRoot;
        }

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static async Task LaunchAsync(string? serverBaseUrl, string loginPath)
    {
#if __WASM__
        await Task.CompletedTask;
        string origin = WasmInterop.GetCurrentOrigin();
        string loginUrl = !string.IsNullOrWhiteSpace(origin)
            ? $"{origin}{loginPath}"
            : loginPath;
        WasmInterop.NavigateTo(loginUrl);
#else
        string baseUrl = string.IsNullOrWhiteSpace(serverBaseUrl) ? DEFAULT_SERVER_URL : serverBaseUrl;
        Uri uri = new($"{baseUrl}{loginPath}");
        await Windows.System.Launcher.LaunchUriAsync(uri);
#endif
    }
}
