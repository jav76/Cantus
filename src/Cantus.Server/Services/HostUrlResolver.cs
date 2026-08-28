using Cantus.Infrastructure.Spotify;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Services;

public sealed class HostUrlResolver : IHostUrlResolver
{
    private readonly IConfiguration _configuration;
    private readonly SpotifyOptions _spotifyOptions;
    private const string DEFAULT_LOCALHOST_BASE = "http://localhost:5000";

    public HostUrlResolver(IConfiguration configuration, IOptions<SpotifyOptions> spotifyOptions)
    {
        _configuration = configuration;
        _spotifyOptions = spotifyOptions.Value;
    }

    public string ResolveBaseUrl(HttpContext? context = null)
    {
        // 1. If context is provided and is a local request (localhost or 127.0.0.1), prioritize the request URL directly for local testing
        if (context is not null && context.Request.Host.HasValue)
        {
            string hostVal = context.Request.Host.Host;
            if (hostVal.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                hostVal.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                string scheme = !string.IsNullOrWhiteSpace(context.Request.Scheme) ? context.Request.Scheme : "http";
                return NormalizeUrl($"{scheme}://{context.Request.Host.Value}");
            }
        }

        // 2. Check explicit environment / configuration variables
        string? envUrl = _configuration["CANTUS_HOST_URL"]
            ?? _configuration["HOST_URL"]
            ?? _configuration["BASE_URL"]
            ?? _configuration["Spotify:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return NormalizeUrl(envUrl);
        }

        // 3. Derive dynamically from incoming request (respects ForwardedHeaders middleware)
        if (context is not null && context.Request.Host.HasValue)
        {
            string scheme = !string.IsNullOrWhiteSpace(context.Request.Scheme) ? context.Request.Scheme : "http";
            string host = context.Request.Host.Value;
            return NormalizeUrl($"{scheme}://{host}");
        }

        // 4. Fallback default
        return DEFAULT_LOCALHOST_BASE;
    }

    public string ResolveSpotifyRedirectUri(HttpContext? context = null)
    {
        // If an explicit custom redirect URI is provided (and it differs from default localhost/127.0.0.1 or host URL is set), use it
        string? explicitRedirect = _configuration["Spotify:RedirectUri"];
        string? envHostUrl = _configuration["CANTUS_HOST_URL"]
            ?? _configuration["HOST_URL"]
            ?? _configuration["BASE_URL"];

        // If explicit redirect is specified and no override host URL is given, return explicit unless it is a standard local default
        if (!string.IsNullOrWhiteSpace(explicitRedirect) &&
            string.IsNullOrWhiteSpace(envHostUrl) &&
            !IsStandardLocalRedirect(explicitRedirect))
        {
            return explicitRedirect;
        }

        string baseUrl = ResolveBaseUrl(context);
        return $"{baseUrl}/api/auth/spotify/callback";
    }

    private static bool IsStandardLocalRedirect(string redirectUri)
    {
        return redirectUri.Equals("http://localhost:5000/api/auth/spotify/callback", StringComparison.OrdinalIgnoreCase) ||
            redirectUri.Equals("http://localhost:5000/api/auth/callback", StringComparison.OrdinalIgnoreCase) ||
            redirectUri.Equals("http://127.0.0.1:5000/api/auth/spotify/callback", StringComparison.OrdinalIgnoreCase) ||
            redirectUri.Equals("http://127.0.0.1:5000/api/auth/callback", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUrl(string url)
    {
        string trimmed = url.Trim();
        if (trimmed.EndsWith('/'))
        {
            trimmed = trimmed.TrimEnd('/');
        }
        return trimmed;
    }
}
