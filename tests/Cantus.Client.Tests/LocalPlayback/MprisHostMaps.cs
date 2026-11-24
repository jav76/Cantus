using System.Collections.Generic;

namespace Cantus.Client.Tests.LocalPlayback;

/// <summary>
/// Property maps as the Linux host's session bus reports them (<c>busctl --user call ... GetAll</c>),
/// after D-Bus values are converted to .NET: <c>s</c> to string, <c>as</c> to string[], <c>t</c> to ulong,
/// <c>x</c> to long, <c>d</c> to double, <c>b</c> to bool, <c>a{sv}</c> to a dictionary.
/// </summary>
internal static class MprisHostMaps
{
    public const string SPOTIFY_BUS_NAME = "org.mpris.MediaPlayer2.spotify";
    public const string BRAVE_BUS_NAME = "org.mpris.MediaPlayer2.brave.instance2";
    public const string CHROMIUM_BUS_NAME = "org.mpris.MediaPlayer2.chromium.instance4004144";
    public const string SPOTIFY_TRACK_ID = "5xfw4Gtv8QZuG3e3jXwekY";

    public static Dictionary<string, object?> SpotifyMetadata() => new()
    {
        ["mpris:trackid"] = "/com/spotify/track/" + SPOTIFY_TRACK_ID,
        ["mpris:length"] = 184000000UL,
        ["mpris:artUrl"] = "https://i.scdn.co/image/ab67616d0000b273cdfe2656c37742ac1beeb7b8",
        ["xesam:album"] = "Reflections",
        ["xesam:albumArtist"] = new[] { "parkbreezy" },
        ["xesam:artist"] = new[] { "parkbreezy" },
        ["xesam:autoRating"] = 0.35,
        ["xesam:discNumber"] = 1,
        ["xesam:title"] = "Ballin",
        ["xesam:trackNumber"] = 4,
        ["xesam:url"] = "https://open.spotify.com/track/" + SPOTIFY_TRACK_ID,
    };

    public static Dictionary<string, object?> SpotifyRoot() => new()
    {
        ["CanQuit"] = true,
        ["CanSetFullscreen"] = false,
        ["CanRaise"] = true,
        ["HasTrackList"] = false,
        ["Identity"] = "Spotify",
        ["DesktopEntry"] = "spotify",
        ["SupportedUriSchemes"] = new[] { "spotify" },
        ["SupportedMimeTypes"] = new string[0],
    };

    public static Dictionary<string, object?> SpotifyPlayer() => new()
    {
        ["PlaybackStatus"] = "Playing",
        ["LoopStatus"] = "Playlist",
        ["Rate"] = 1.0,
        ["Shuffle"] = true,
        ["Metadata"] = SpotifyMetadata(),
        ["Volume"] = 1.0,
        ["Position"] = 112283000L,
        ["MinimumRate"] = 1.0,
        ["MaximumRate"] = 1.0,
        ["CanGoNext"] = true,
        ["CanGoPrevious"] = true,
        ["CanPlay"] = true,
        ["CanPause"] = true,
        ["CanSeek"] = true,
        ["CanControl"] = true,
    };

    /// <summary>Brave and Chromium (VS Code) idle entries: Stopped, a zero length and nothing else.</summary>
    public static Dictionary<string, object?> IdleBrowserMetadata() => new()
    {
        ["mpris:length"] = 0L,
    };

    public static Dictionary<string, object?> IdleBrowserRoot(string identity) => new()
    {
        ["CanQuit"] = false,
        ["CanRaise"] = false,
        ["HasTrackList"] = false,
        ["Identity"] = identity,
        ["SupportedMimeTypes"] = new string[0],
        ["SupportedUriSchemes"] = new string[0],
    };

    public static Dictionary<string, object?> IdleBrowserPlayer() => new()
    {
        ["CanControl"] = true,
        ["CanGoNext"] = false,
        ["CanGoPrevious"] = false,
        ["CanPause"] = false,
        ["CanPlay"] = false,
        ["CanSeek"] = false,
        ["MaximumRate"] = 1.0,
        ["Metadata"] = IdleBrowserMetadata(),
        ["MinimumRate"] = 1.0,
        ["PlaybackStatus"] = "Stopped",
        ["Position"] = 0L,
        ["Rate"] = 1.0,
        ["Volume"] = 1.0,
    };
}
