using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Cantus.Client.Views;

/// <summary>
/// The desktop app's Server setting: what the settings area shows, the dialog that changes it,
/// and the restart that applies a change. The WASM client is served by its server and has none.
/// </summary>
internal static class ServerSettingsEntry
{
    private const string APPIMAGE_VARIABLE = "APPIMAGE";
    private const string DOTNET_HOST_NAME = "dotnet";

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger(nameof(ServerSettingsEntry));
    private static readonly HttpClient _httpClient = new();
    private static readonly SolidColorBrush _reachableBrush = new(ColorHelper.FromArgb(0xFF, 0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush _warningBrush = new(ColorHelper.FromArgb(0xFF, 0xF5, 0x9E, 0x0B));

    public static Visibility SectionVisibility =>
#if __WASM__
        Visibility.Collapsed;
#else
        Visibility.Visible;
#endif

    public static string CurrentServerText => ClientStartupOptions.Selection?.BaseUrl ?? string.Empty;

    public static ServerSetupViewModel CreateViewModel()
    {
        ServerSettingsStore settings = new(new LocalSettingsStore());
        ServerSelection current = ClientStartupOptions.Selection ?? ClientStartupOptions.ResolveServer(settings);

        return new ServerSetupViewModel(
            settings,
            current,
            new ServerProbe(_httpClient),
            ForgetCredentials,
#if __WASM__
            localServer: null);
#else
            LocalServerManager.ForThisApp);
#endif
    }

    public static Brush GetStatusBrush(ServerCheckState state, ThemeManager theme) => state switch
    {
        ServerCheckState.Reachable => _reachableBrush,
        ServerCheckState.Unreachable => _warningBrush,
        _ => theme.TextMutedBrush,
    };

    public static async Task OpenAsync(XamlRoot? xamlRoot)
    {
        try
        {
            ServerSetupViewModel setup = CreateViewModel();
            ServerSettingsDialog dialog = new(setup, ThemeManager.Instance);
            if (xamlRoot is not null)
            {
                dialog.XamlRoot = xamlRoot;
            }

            await dialog.ShowAsync();
            if (dialog.Result != ServerSetupResult.Changed || TryRestart())
            {
                return;
            }

            ContentDialog notice = new()
            {
                Title = "Restart Cantus",
                Content = "The new server is saved. Close Cantus and open it again to connect to it.",
                CloseButtonText = "OK",
            };
            if (xamlRoot is not null)
            {
                notice.XamlRoot = xamlRoot;
            }

            await notice.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Changing the server failed");
        }
    }

    // The saved Session and Display token were issued by the old server and mean nothing to another one.
    private static void ForgetCredentials()
    {
        new DisplayCredentialStore(new LocalSettingsStore()).Clear();
        SignalRPlaybackClient.ForgetPersistedSession();
    }

    private static bool TryRestart()
    {
#if __WASM__
        return false;
#else
        try
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string? executable = Environment.ProcessPath;

            // An AppImage unmounts its files when the running app exits, so the AppImage itself is started again.
            string? appImage = Environment.GetEnvironmentVariable(APPIMAGE_VARIABLE);
            bool isAppImage = !string.IsNullOrWhiteSpace(appImage) && File.Exists(appImage);
            if (isAppImage)
            {
                executable = appImage;
            }

            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            // Under "dotnet Cantus.Client.dll" the first argument is the assembly, which the host needs back.
            bool isDotnetHost = !isAppImage
                && string.Equals(Path.GetFileNameWithoutExtension(executable), DOTNET_HOST_NAME, StringComparison.OrdinalIgnoreCase);

            ProcessStartInfo startInfo = new(executable) { UseShellExecute = false };
            foreach (string argument in isDotnetHost ? arguments : arguments.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            Application.Current.Exit();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restart the app after a server change");

            return false;
        }
#endif
    }
}
