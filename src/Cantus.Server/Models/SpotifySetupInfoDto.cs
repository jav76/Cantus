namespace Cantus.Server.Models;

/// <summary>
/// What the Connect Spotify setup screen needs to guide a user through creating their own app,
/// and whether the client may offer a Local Session instead.
/// </summary>
public sealed record SpotifySetupInfoDto
{
    public const string DASHBOARD_CREATE_URL = "https://developer.spotify.com/dashboard/create";

    public bool ServerClientIdConfigured { get; init; }
    public bool UserClientIdsAllowed { get; init; }
    public required string RedirectUri { get; init; }

    /// <summary>
    /// False when Spotify will refuse <see cref="RedirectUri"/>: plain http on anything but a
    /// loopback IP literal, which includes <c>localhost</c> and LAN addresses.
    /// </summary>
    public bool RedirectUriAccepted { get; init; }

    public string DashboardUrl { get; init; } = DASHBOARD_CREATE_URL;

    public bool LocalSessionsEnabled { get; init; }

    /// <summary>True when creating a Local Session needs a join code. The code itself is never sent.</summary>
    public bool LocalSessionJoinCodeRequired { get; init; }
}
