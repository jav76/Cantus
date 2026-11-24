using System;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Cantus.Client.Views;

/// <summary>
/// Starts "Use this computer's players" from any button: tries right away when the server needs no join
/// code, and shows <see cref="LocalSessionDialog"/> for the join code or to explain a refusal.
/// </summary>
internal static class LocalSessionSignIn
{
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger(nameof(LocalSessionSignIn));

    public static async Task StartAsync(SessionViewModel? session, XamlRoot? xamlRoot)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            LocalSessionSetupViewModel setup = session.LocalSetup;
            setup.Reset(session.LocalJoinCodeRequired, session.JoinCodePrefill);
            if (!setup.IsJoinCodeVisible && await setup.ConnectAsync())
            {
                return;
            }

            LocalSessionDialog dialog = new(setup, ThemeManager.Instance);
            if (xamlRoot is not null)
            {
                dialog.XamlRoot = xamlRoot;
            }

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error using this computer's players");
        }
    }
}
