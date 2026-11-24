using System;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Reads a Spotify track ID from what a player publishes: <c>/com/spotify/track/&lt;id&gt;</c> or
/// <c>spotify:track:&lt;id&gt;</c> track IDs, or an <c>https://open.spotify.com/track/&lt;id&gt;</c> URL.
/// Episodes, ads and local files are not tracks and yield null.
/// </summary>
public static class SpotifyTrackIdParser
{
    public const int TRACK_ID_LENGTH = 22;

    private const string OBJECT_PATH_PREFIX = "/com/spotify/track/";
    private const string URI_PREFIX = "spotify:track:";
    private const string WEB_HOST = "open.spotify.com";
    private const string WEB_TRACK_SEGMENT = "track";

    public static string? Parse(string? trackId, string? url) => FromTrackId(trackId) ?? FromUrl(url);

    public static string? FromTrackId(string? trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return null;
        }

        string value = trackId.Trim();
        if (value.StartsWith(OBJECT_PATH_PREFIX, StringComparison.Ordinal))
        {
            return Validate(value[OBJECT_PATH_PREFIX.Length..]);
        }

        if (value.StartsWith(URI_PREFIX, StringComparison.Ordinal))
        {
            return Validate(value[URI_PREFIX.Length..]);
        }

        return null;
    }

    public static string? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.Equals(uri.Host, WEB_HOST, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[^2] != WEB_TRACK_SEGMENT)
        {
            return null;
        }

        return Validate(segments[^1]);
    }

    public static bool IsValidTrackId(string? id)
    {
        if (id is null || id.Length != TRACK_ID_LENGTH)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Validate(string candidate) => IsValidTrackId(candidate) ? candidate : null;
}
