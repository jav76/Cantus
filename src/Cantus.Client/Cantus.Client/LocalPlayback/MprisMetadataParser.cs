using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Turns an MPRIS <c>Metadata</c> map, with D-Bus values already converted to plain .NET values, into a
/// <see cref="LocalTrack"/>. Players disagree on value types (Spotify sends <c>mpris:length</c> as
/// <c>uint64</c> and <c>mpris:trackid</c> as a string; Chromium sends <c>int64</c>), so every reader is
/// lenient about the numeric or string shape.
/// </summary>
public static class MprisMetadataParser
{
    public const string KEY_TRACK_ID = "mpris:trackid";
    public const string KEY_LENGTH = "mpris:length";
    public const string KEY_ART_URL = "mpris:artUrl";
    public const string KEY_TITLE = "xesam:title";
    public const string KEY_ARTIST = "xesam:artist";
    public const string KEY_ALBUM = "xesam:album";
    public const string KEY_URL = "xesam:url";

    private const string ARTIST_SEPARATOR = ", ";

    /// <summary>Null when the map names neither a title nor an artist.</summary>
    public static LocalTrack? Parse(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        string title = ReadString(metadata, KEY_TITLE)?.Trim() ?? string.Empty;
        string artist = JoinArtists(Get(metadata, KEY_ARTIST));
        if (title.Length == 0 && artist.Length == 0)
        {
            return null;
        }

        string? album = ReadString(metadata, KEY_ALBUM)?.Trim();
        return new LocalTrack(
            title,
            artist,
            string.IsNullOrEmpty(album) ? null : album,
            ReadMicroseconds(metadata, KEY_LENGTH),
            ReadHttpUrl(metadata, KEY_ART_URL),
            SpotifyTrackIdParser.Parse(ReadString(metadata, KEY_TRACK_ID), ReadString(metadata, KEY_URL)));
    }

    /// <summary>The raw <c>mpris:trackid</c>, which MPRIS <c>SetPosition</c> needs as an object path.</summary>
    public static string? ReadTrackObjectPath(IReadOnlyDictionary<string, object?>? metadata)
    {
        string? trackId = metadata is null ? null : ReadString(metadata, KEY_TRACK_ID);
        return string.IsNullOrWhiteSpace(trackId) ? null : trackId;
    }

    public static TimeSpan MicrosecondsToTimeSpan(object? value)
    {
        long? microseconds = ToInt64(value);
        return microseconds is > 0 ? TimeSpan.FromTicks(microseconds.Value * 10) : TimeSpan.Zero;
    }

    public static long? ToInt64(object? value) => value switch
    {
        long l => l,
        ulong ul => ul > long.MaxValue ? long.MaxValue : (long)ul,
        int i => i,
        uint ui => ui,
        short s => s,
        ushort us => us,
        byte b => b,
        double d when double.IsFinite(d) => (long)d,
        _ => null,
    };

    private static object? Get(IReadOnlyDictionary<string, object?> metadata, string key) =>
        metadata.TryGetValue(key, out object? value) ? value : null;

    private static string? ReadString(IReadOnlyDictionary<string, object?> metadata, string key) =>
        Get(metadata, key) switch
        {
            string s => s,
            IEnumerable<string> values => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            _ => null,
        };

    private static TimeSpan ReadMicroseconds(IReadOnlyDictionary<string, object?> metadata, string key) =>
        MicrosecondsToTimeSpan(Get(metadata, key));

    private static string? ReadHttpUrl(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        string? raw = ReadString(metadata, key)?.Trim();
        if (string.IsNullOrEmpty(raw)
            || !Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return raw;
    }

    private static string JoinArtists(object? value)
    {
        IEnumerable<string?> names = value switch
        {
            string single => [single],
            IEnumerable<string> many => many,
            IEnumerable items => items.Cast<object?>().Select(item => item as string),
            _ => [],
        };

        return string.Join(
            ARTIST_SEPARATOR,
            names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!.Trim()));
    }
}
