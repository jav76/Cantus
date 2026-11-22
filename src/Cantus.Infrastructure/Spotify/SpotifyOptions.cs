namespace Cantus.Infrastructure.Spotify;

public sealed class SpotifyOptions
{
    public const string SECTION_NAME = "Spotify";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:5000/api/auth/spotify/callback";

    /// <summary>
    /// Whether a user may sign in with their own Spotify app's Client ID. Unset means
    /// allowed only when the server has no <see cref="ClientId"/> of its own, so an
    /// existing server keeps Spotify's allowlist as the gate on who can sign in.
    /// </summary>
    public bool? AllowUserClientIds { get; set; }

    public bool ServerClientIdConfigured => !string.IsNullOrWhiteSpace(ClientId);

    public bool UserClientIdsAllowed => AllowUserClientIds ?? !ServerClientIdConfigured;

    public List<string> Scopes { get; set; } =
    [
        "user-read-playback-state",
        "user-read-currently-playing",
        "user-modify-playback-state",
        "user-read-email",
        "user-read-private"
    ];
}
