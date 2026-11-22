namespace Cantus.Server.Models;

/// <summary>What the Connect Spotify setup screen needs to guide a user through creating their own app.</summary>
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
}
