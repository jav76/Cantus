namespace Cantus.Infrastructure.Spotify;

public sealed class SpotifyOptions
{
    public const string SectionName = "Spotify";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:5000/api/auth/spotify/callback";
    public List<string> Scopes { get; set; } =
    [
        "user-read-playback-state",
        "user-read-currently-playing",
        "user-read-email",
        "user-read-private"
    ];
}
