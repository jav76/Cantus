using System;

namespace Cantus.Client.Services;

/// <summary>
/// Startup values supplied by the host process (desktop CLI arguments or
/// environment variables) before any page or view model is constructed.
/// </summary>
public static class ClientStartupOptions
{
    private const string HUB_PATH = "/hubs/playback";

    /// <summary>
    /// Fully-qualified SignalR hub URL the client should connect to, or null
    /// to use the default resolution (the page origin on WASM, localhost
    /// otherwise).
    /// </summary>
    public static string? ServerUrl { get; private set; }

    /// <summary>
    /// Records the Cantus server address the client should connect to.
    /// Accepts either a base address (<c>http://192.168.0.10:5000</c>) or a
    /// full hub URL; the hub path is appended when missing, so callers can
    /// pass the same address they would open in a browser. Values that are not
    /// absolute http/https URLs are rejected and leave the default resolution
    /// in place rather than preventing the client from starting.
    /// </summary>
    /// <returns>True when the value was accepted.</returns>
    public static bool TrySetServerUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        string trimmed = rawUrl.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        ServerUrl = trimmed.Contains(HUB_PATH, StringComparison.Ordinal)
            ? trimmed
            : $"{trimmed.TrimEnd('/')}{HUB_PATH}";

        return true;
    }

    internal static void ResetForTesting()
    {
        ServerUrl = null;
    }
}
