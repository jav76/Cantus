namespace Cantus.Client.ViewModels;

public enum AccountMenuItemKind
{
    /// <summary>Non-interactive text, such as who is signed in.</summary>
    Header,
    Separator,
    Command,
}

public enum AccountMenuAction
{
    None,
    ConnectSpotifyInstead,
    UseLocalPlayersInstead,
    SignOut,
}

/// <summary>One entry of the account menu the account buttons open while a Session is held.</summary>
public sealed record AccountMenuItem(AccountMenuItemKind Kind, string Text, AccountMenuAction Action)
{
    public static AccountMenuItem Separator { get; } = new(AccountMenuItemKind.Separator, string.Empty, AccountMenuAction.None);

    public static AccountMenuItem Header(string text) => new(AccountMenuItemKind.Header, text, AccountMenuAction.None);

    public static AccountMenuItem Command(string text, AccountMenuAction action) => new(AccountMenuItemKind.Command, text, action);
}
