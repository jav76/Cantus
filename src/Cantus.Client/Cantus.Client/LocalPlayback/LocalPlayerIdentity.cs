using System;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// The stable app identity of an MPRIS player, used for the per-player switch: the <c>DesktopEntry</c>
/// lower-cased when the player sets one, else the bus-name suffix without a trailing
/// <c>.instance&lt;digits&gt;</c> (<c>org.mpris.MediaPlayer2.brave.instance2</c> is <c>brave</c>).
/// </summary>
public static class LocalPlayerIdentity
{
    public const string MPRIS_BUS_PREFIX = "org.mpris.MediaPlayer2.";

    private const string INSTANCE_MARKER = ".instance";

    public static bool IsMprisBusName(string? busName) =>
        busName is not null
        && busName.Length > MPRIS_BUS_PREFIX.Length
        && busName.StartsWith(MPRIS_BUS_PREFIX, StringComparison.Ordinal);

    public static string ResolveAppId(string busName, string? desktopEntry)
    {
        if (!string.IsNullOrWhiteSpace(desktopEntry))
        {
            return desktopEntry.Trim().ToLowerInvariant();
        }

        string suffix = busName.StartsWith(MPRIS_BUS_PREFIX, StringComparison.Ordinal)
            ? busName[MPRIS_BUS_PREFIX.Length..]
            : busName;
        return StripInstanceSuffix(suffix);
    }

    public static string ResolveAppName(string? identity, string appId) =>
        string.IsNullOrWhiteSpace(identity) ? appId : identity.Trim();

    private static string StripInstanceSuffix(string suffix)
    {
        int marker = suffix.LastIndexOf(INSTANCE_MARKER, StringComparison.Ordinal);
        if (marker <= 0)
        {
            return suffix;
        }

        ReadOnlySpan<char> digits = suffix.AsSpan(marker + INSTANCE_MARKER.Length);
        if (digits.IsEmpty)
        {
            return suffix;
        }

        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return suffix;
            }
        }

        return suffix[..marker];
    }
}
