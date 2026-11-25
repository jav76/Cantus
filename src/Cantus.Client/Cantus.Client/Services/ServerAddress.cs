using System;

namespace Cantus.Client.Services;

/// <summary>Parses the Cantus server address a user types or passes at start-up.</summary>
public static class ServerAddress
{
    public const string DEFAULT_BASE_URL = "http://127.0.0.1:5000";
    public const string HUB_PATH = "/hubs/playback";
    public const string HEALTH_PATH = "/api/health";

    /// <summary>
    /// Reduces an address to the server's base URL: the address a browser would open, without a
    /// trailing slash, hub path, query string or fragment. Only absolute http and https URLs pass.
    /// </summary>
    public static bool TryNormalize(string? rawUrl, out string baseUrl)
    {
        baseUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host))
        {
            return false;
        }

        string path = parsed.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(HUB_PATH, StringComparison.Ordinal))
        {
            path = path.Substring(0, path.Length - HUB_PATH.Length);
        }

        baseUrl = $"{parsed.GetLeftPart(UriPartial.Authority)}{path}";

        return true;
    }

    public static string ToHubUrl(string baseUrl) => $"{baseUrl}{HUB_PATH}";

    public static bool AreSame(string firstBaseUrl, string secondBaseUrl) =>
        string.Equals(firstBaseUrl, secondBaseUrl, StringComparison.OrdinalIgnoreCase);
}
