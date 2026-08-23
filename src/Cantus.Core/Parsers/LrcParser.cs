using System.Globalization;
using Cantus.Core.Models;

namespace Cantus.Core.Parsers;

public static class LrcParser
{
    public static SyncedLyrics Parse(
        string? rawLrc,
        string trackId = "",
        string title = "",
        string artist = "",
        string? album = null,
        string? plainLyrics = null)
    {
        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return new SyncedLyrics
            {
                TrackId = trackId,
                Title = title,
                Artist = artist,
                Album = album,
                Lines = [],
                IsSynced = false,
                IsInstrumental = false,
                PlainLyrics = plainLyrics
            };
        }

        var lines = ParseLines(rawLrc);
        return new SyncedLyrics
        {
            TrackId = trackId,
            Title = title,
            Artist = artist,
            Album = album,
            Lines = lines,
            IsSynced = lines.Count > 0,
            IsInstrumental = false,
            PlainLyrics = plainLyrics
        };
    }

    public static IReadOnlyList<LyricLine> ParseLines(string? rawLrc)
    {
        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return [];
        }

        var result = new List<LyricLine>();
        var span = rawLrc.AsSpan();

        foreach (var rawLine in span.EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            // Extract all timestamps from the line
            var timestamps = new List<TimeSpan>();
            int textStartIndex = 0;

            while (textStartIndex < line.Length && line[textStartIndex] == '[')
            {
                int closeBracket = line[textStartIndex..].IndexOf(']');
                if (closeBracket <= 0)
                {
                    break;
                }

                int tagEnd = textStartIndex + closeBracket;
                var tagContent = line.Slice(textStartIndex + 1, closeBracket - 1);

                if (TryParseTimestamp(tagContent, out var timestamp))
                {
                    timestamps.Add(timestamp);
                    textStartIndex = tagEnd + 1;
                }
                else
                {
                    // Metadata tag like [ar:Artist], ignore and move forward
                    textStartIndex = tagEnd + 1;
                }
            }

            if (timestamps.Count > 0)
            {
                var text = line[textStartIndex..].Trim().ToString();
                foreach (var ts in timestamps)
                {
                    result.Add(new LyricLine(ts, text));
                }
            }
        }

        result.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    public static bool TryParseTimestamp(ReadOnlySpan<char> span, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        span = span.Trim();

        int firstColon = span.IndexOf(':');
        if (firstColon <= 0)
        {
            return false;
        }

        var minutesSpan = span[..firstColon];
        if (!int.TryParse(minutesSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) || minutes < 0)
        {
            return false;
        }

        var rest = span[(firstColon + 1)..];
        int dotIndex = rest.IndexOfAny('.', ':');

        if (dotIndex < 0)
        {
            if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds < 0 || seconds >= 60)
            {
                return false;
            }

            timestamp = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        var secondsSpan = rest[..dotIndex];
        if (!int.TryParse(secondsSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec) || sec < 0 || sec >= 60)
        {
            return false;
        }

        var fractionSpan = rest[(dotIndex + 1)..];
        if (fractionSpan.IsEmpty)
        {
            timestamp = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(sec);
            return true;
        }

        if (!int.TryParse(fractionSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fraction) || fraction < 0)
        {
            return false;
        }

        int milliseconds = fractionSpan.Length switch
        {
            1 => fraction * 100,
            2 => fraction * 10,
            3 => fraction,
            _ => (int)(fraction / Math.Pow(10, fractionSpan.Length - 3))
        };

        timestamp = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}
