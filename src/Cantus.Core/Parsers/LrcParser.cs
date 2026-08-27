using System.Globalization;
using System.Text;
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

        IReadOnlyList<LyricLine> lines = ParseLines(rawLrc);
        string? effectivePlainLyrics = !string.IsNullOrWhiteSpace(plainLyrics)
            ? plainLyrics
            : (lines.Count > 0 ? string.Join("\n", lines.Select(l => l.Text)) : null);

        return new SyncedLyrics
        {
            TrackId = trackId,
            Title = title,
            Artist = artist,
            Album = album,
            Lines = lines,
            IsSynced = lines.Count > 0,
            IsInstrumental = false,
            PlainLyrics = effectivePlainLyrics
        };
    }

    public static IReadOnlyList<LyricLine> ParseLines(string? rawLrc)
    {
        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return [];
        }

        List<LyricLine> result = new();
        ReadOnlySpan<char> span = rawLrc.AsSpan();

        foreach (ReadOnlySpan<char> rawLine in span.EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            List<TimeSpan> timestamps = new();
            int textStartIndex = 0;

            while (textStartIndex < line.Length && line[textStartIndex] == '[')
            {
                int closeBracket = line[textStartIndex..].IndexOf(']');
                if (closeBracket <= 0)
                {
                    break;
                }

                int tagEnd = textStartIndex + closeBracket;
                ReadOnlySpan<char> tagContent = line.Slice(textStartIndex + 1, closeBracket - 1);

                if (TryParseTimestamp(tagContent, out TimeSpan timestamp))
                {
                    timestamps.Add(timestamp);
                    textStartIndex = tagEnd + 1;
                }
                else
                {
                    textStartIndex = tagEnd + 1;
                }
            }

            if (timestamps.Count > 0)
            {
                (string cleanText, IReadOnlyList<LyricSyllable>? syllables) = ParseInlineSyllables(
                    line[textStartIndex..],
                    timestamps[0]);

                foreach (TimeSpan ts in timestamps)
                {
                    result.Add(new LyricLine(ts, cleanText, syllables));
                }
            }
        }

        result.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    public static (string CleanText, IReadOnlyList<LyricSyllable>? Syllables) ParseInlineSyllables(
        ReadOnlySpan<char> span,
        TimeSpan lineTimestamp)
    {
        if (span.IsEmpty)
        {
            return (string.Empty, null);
        }

        int firstAngle = span.IndexOf('<');
        if (firstAngle < 0)
        {
            return (span.Trim().ToString(), null);
        }

        List<LyricSyllable> syllables = new();
        StringBuilder cleanBuilder = new();
        int idx = 0;
        TimeSpan currentSyllableTime = lineTimestamp;

        if (firstAngle > 0)
        {
            string prefix = span[..firstAngle].Trim().ToString();
            if (!string.IsNullOrEmpty(prefix))
            {
                cleanBuilder.Append(prefix);
                syllables.Add(new LyricSyllable(lineTimestamp, TimeSpan.Zero, prefix));
            }
            idx = firstAngle;
        }

        while (idx < span.Length)
        {
            if (span[idx] == '<')
            {
                int closeAngle = span[idx..].IndexOf('>');
                if (closeAngle > 0)
                {
                    ReadOnlySpan<char> tag = span.Slice(idx + 1, closeAngle - 1);
                    if (TryParseTimestamp(tag, out TimeSpan tagTs))
                    {
                        currentSyllableTime = tagTs;
                        idx += closeAngle + 1;

                        int nextAngle = span[idx..].IndexOf('<');
                        int wordLen = nextAngle >= 0 ? nextAngle : span.Length - idx;
                        string wordText = span.Slice(idx, wordLen).Trim().ToString();
                        idx += wordLen;

                        if (!string.IsNullOrEmpty(wordText))
                        {
                            if (cleanBuilder.Length > 0 && cleanBuilder[^1] != ' ')
                            {
                                cleanBuilder.Append(' ');
                            }
                            cleanBuilder.Append(wordText);
                            syllables.Add(new LyricSyllable(currentSyllableTime, TimeSpan.Zero, wordText));
                        }
                        continue;
                    }
                }
            }

            cleanBuilder.Append(span[idx]);
            idx++;
        }

        if (syllables.Count > 1)
        {
            for (int i = 0; i < syllables.Count - 1; i++)
            {
                LyricSyllable cur = syllables[i];
                LyricSyllable next = syllables[i + 1];
                TimeSpan dur = next.Timestamp > cur.Timestamp ? next.Timestamp - cur.Timestamp : TimeSpan.Zero;
                syllables[i] = new LyricSyllable(cur.Timestamp, dur, cur.Text);
            }
        }

        return (cleanBuilder.ToString().Trim(), syllables.Count > 0 ? syllables : null);
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

        ReadOnlySpan<char> minutesSpan = span[..firstColon];
        if (!int.TryParse(minutesSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) || minutes < 0)
        {
            return false;
        }

        ReadOnlySpan<char> rest = span[(firstColon + 1)..];
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

        ReadOnlySpan<char> secondsSpan = rest[..dotIndex];
        if (!int.TryParse(secondsSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec) || sec < 0 || sec >= 60)
        {
            return false;
        }

        ReadOnlySpan<char> fractionSpan = rest[(dotIndex + 1)..];
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
