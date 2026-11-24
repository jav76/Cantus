using System;
using System.Threading.Tasks;
using Cantus.Client.Icons;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Cantus.Client.Views;

/// <summary>
/// The account buttons: while a Session is held they open a menu built from
/// <see cref="SessionViewModel.AccountMenuItems"/>; signed out they start the Spotify sign-in.
/// </summary>
internal static class AccountMenu
{
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger(nameof(AccountMenu));

    public static async Task OpenAsync(SessionViewModel? session, FrameworkElement anchor)
    {
        if (session is null || !session.HasAccountMenu)
        {
            await SpotifySignIn.StartAsync(session, anchor.XamlRoot);
            return;
        }

        MenuFlyout flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (AccountMenuItem item in session.AccountMenuItems)
        {
            flyout.Items.Add(CreateItem(session, item, anchor.XamlRoot));
        }

        flyout.ShowAt(anchor);
    }

    public static async Task RunAsync(SessionViewModel session, AccountMenuAction action, XamlRoot? xamlRoot)
    {
        try
        {
            switch (action)
            {
                case AccountMenuAction.SignOut:
                    await session.LogoutAsync();
                    break;
                case AccountMenuAction.ConnectSpotifyInstead:
                    await session.SwitchToSpotifyAsync(() => SpotifySignIn.StartAsync(session, xamlRoot));
                    break;
                case AccountMenuAction.UseLocalPlayersInstead:
                    await LocalSessionSignIn.StartAsync(session, xamlRoot);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account action {Action} failed", action);
        }
    }

    private static MenuFlyoutItemBase CreateItem(SessionViewModel session, AccountMenuItem item, XamlRoot? xamlRoot)
    {
        switch (item.Kind)
        {
            case AccountMenuItemKind.Separator:
                return new MenuFlyoutSeparator();
            case AccountMenuItemKind.Header:
                return new MenuFlyoutItem { Text = item.Text, IsEnabled = false };
            default:
                MenuFlyoutItem command = new() { Text = item.Text, Icon = CreateIcon(item.Action) };
                AccountMenuAction action = item.Action;
                command.Click += async (_, _) => await RunAsync(session, action, xamlRoot);
                return command;
        }
    }

    private static IconElement? CreateIcon(AccountMenuAction action) => action switch
    {
        AccountMenuAction.SignOut => new CantusIcon { Kind = CantusIconKind.SignOut, Size = 16 },
        AccountMenuAction.ConnectSpotifyInstead => new CantusIcon { Kind = CantusIconKind.SpotifyConnect, Size = 16 },
        AccountMenuAction.UseLocalPlayersInstead => new CantusIcon { Kind = CantusIconKind.Display, Size = 16 },
        _ => null,
    };
}
