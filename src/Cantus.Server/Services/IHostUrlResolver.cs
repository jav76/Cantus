using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public interface IHostUrlResolver
{
    /// <summary>
    /// Resolves the canonical public base URL (e.g. "https://lyrics.example.com" or "http://192.168.1.50:5000").
    /// Prioritizes CANTUS_HOST_URL / HOST_URL / BASE_URL environment variables, then incoming HTTP request host/proto, and falls back to "http://localhost:5000".
    /// </summary>
    string ResolveBaseUrl(HttpContext? context = null);

    /// <summary>
    /// Resolves the effective Spotify OAuth callback URI (e.g. "https://lyrics.example.com/api/auth/spotify/callback").
    /// </summary>
    string ResolveSpotifyRedirectUri(HttpContext? context = null);
}
