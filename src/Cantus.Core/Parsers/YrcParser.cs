using System.Globalization;
using System.Text;
using Cantus.Core.Models;

namespace Cantus.Core.Parsers;

/// <summary>
/// Parses NetEase word-by-word ("yrc") lyrics, where each line is
/// <c>[lineStartMs,lineDurationMs]</c> followed by tokens of the form
/// <c>(wordStartMs,wordDurationMs,flag)text</c>. All times are absolute milliseconds.
/// Every non-blank input line yields exactly one <see cref="LyricLine"/>: a line whose
/// tokens cannot be read keeps line-level timing with no syllables, and the parser never throws.
/// Syllable text is kept exactly as sent (a word's trailing space stays on it), so adjacent
/// syllables of one word can be told apart from separate words.
/// </summary>
public static class YrcParser
{
    private const int HEADER_FIELD_COUNT = 2;
    private const int TOKEN_FIELD_COUNT = 3;

    private const char HEADER_OPEN = '[';
    private const char HEADER_CLOSE = ']';
    private const char TOKEN_OPEN = '(';
    private const char TOKEN_CLOSE = ')';
    private const char FIELD_SEPARATOR = ',';

    public static IReadOnlyList<LyricLine> ParseLines(string? rawYrc)
    {
        if (string.IsNullOrWhiteSpace(rawYrc))
        {
            return [];
        }

        List<LyricLine> result = new();
        TimeSpan previousTimestamp = TimeSpan.Zero;

        foreach (ReadOnlySpan<char> rawLine in rawYrc.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.TrimStart();
            if (line.IsWhiteSpace())
            {
                continue;
            }

            LyricLine parsed = ParseLine(line, previousTimestamp);
            result.Add(parsed);
            previousTimestamp = parsed.Timestamp;
        }

        // OrderBy is stable, so lines sharing a timestamp (e.g. credits at [0,0]) keep their order.
        return result
            .OrderBy(l => l.Timestamp)
            .ToList();
    }

    private static LyricLine ParseLine(ReadOnlySpan<char> line, TimeSpan previousTimestamp)
    {
        TimeSpan? lineStart = ReadHeader(line, out int bodyStart);
        ReadOnlySpan<char> body = line[bodyStart..];

        List<LyricSyllable>? syllables = TryParseTokens(body);
        if (syllables is null)
        {
            string fallbackText = StripTokenHeaders(body, out TimeSpan? firstTokenStart);
            return new LyricLine(lineStart ?? firstTokenStart ?? previousTimestamp, fallbackText);
        }

        TimeSpan timestamp = lineStart
            ?? (syllables.Count > 0 ? syllables[0].Timestamp : previousTimestamp);
        string text = string.Concat(syllables.Select(s => s.Text)).Trim();

        return new LyricLine(timestamp, text, syllables.Count > 0 ? syllables : null);
    }

    /// <summary>
    /// Returns the line start from a <c>[start,duration]</c> header, or null when the header is
    /// missing or unreadable. A bracketed but unreadable header is still skipped so it does not
    /// leak into the line text, but only when its <c>]</c> comes before any whitespace or bracket,
    /// so a header missing its <c>]</c> never swallows lyric text up to a later one.
    /// </summary>
    private static TimeSpan? ReadHeader(ReadOnlySpan<char> line, out int bodyStart)
    {
        bodyStart = 0;
        if (line.IsEmpty || line[0] != HEADER_OPEN)
        {
            return null;
        }

        int close = FindHeaderClose(line);
        if (close < 0)
        {
            return null;
        }

        bodyStart = close + 1;
        Span<int> fields = stackalloc int[HEADER_FIELD_COUNT];
        return TryParseFields(line[1..close], fields) ? TimeSpan.FromMilliseconds(fields[0]) : null;
    }

    /// <summary>
    /// Returns the syllables of a well-formed token body, or null when any part of it is
    /// malformed: text before the first token, an unreadable token header, or a token that
    /// starts before the previous one.
    /// </summary>
    private static List<LyricSyllable>? TryParseTokens(ReadOnlySpan<char> body)
    {
        List<LyricSyllable> syllables = new();
        if (body.IsWhiteSpace())
        {
            return syllables;
        }

        TimeSpan previousStart = TimeSpan.Zero;
        int index = 0;
        while (index < body.Length)
        {
            if (!TryReadToken(body[index..], out TimeSpan start, out TimeSpan duration, out int headerLength) ||
                start < previousStart)
            {
                return null;
            }

            int textStart = index + headerLength;
            int textEnd = FindNextTokenStart(body, textStart);
            if (textEnd > textStart)
            {
                syllables.Add(new LyricSyllable(start, duration, body[textStart..textEnd].ToString()));
            }

            previousStart = start;
            index = textEnd;
        }

        return syllables;
    }

    private static bool TryReadToken(
        ReadOnlySpan<char> span,
        out TimeSpan start,
        out TimeSpan duration,
        out int headerLength)
    {
        start = TimeSpan.Zero;
        duration = TimeSpan.Zero;
        headerLength = 0;

        int close = FindTokenClose(span);
        if (close < 0)
        {
            return false;
        }

        Span<int> fields = stackalloc int[TOKEN_FIELD_COUNT];
        if (!TryParseFields(span[1..close], fields))
        {
            return false;
        }

        start = TimeSpan.FromMilliseconds(fields[0]);
        duration = TimeSpan.FromMilliseconds(fields[1]);
        headerLength = close + 1;
        return true;
    }

    /// <summary>
    /// Parses exactly <c>fields.Length</c> comma-separated unsigned integers. Signs, spaces and
    /// values beyond <see cref="int.MaxValue"/> are rejected.
    /// </summary>
    private static bool TryParseFields(ReadOnlySpan<char> content, Span<int> fields)
    {
        Span<Range> ranges = stackalloc Range[fields.Length + 1];
        int count = content.Split(ranges, FIELD_SEPARATOR);
        if (count != fields.Length)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!int.TryParse(content[ranges[i]], NumberStyles.None, CultureInfo.InvariantCulture, out fields[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static int FindHeaderClose(ReadOnlySpan<char> line)
    {
        for (int i = 1; i < line.Length; i++)
        {
            char current = line[i];
            if (current == HEADER_CLOSE)
            {
                return i;
            }

            if (char.IsWhiteSpace(current) || current == HEADER_OPEN || current == TOKEN_OPEN)
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns the index of the <c>)</c> closing a token-shaped fragment at the start of the span:
    /// an opening parenthesis, a digit, then only digits and commas with at least one comma.
    /// Returns -1 at the first other character, which keeps the scan short and leaves
    /// parenthesised lyric text such as <c>(2,000 miles)</c> alone.
    /// </summary>
    private static int FindTokenClose(ReadOnlySpan<char> span)
    {
        if (span.Length < 2 || span[0] != TOKEN_OPEN || !char.IsAsciiDigit(span[1]))
        {
            return -1;
        }

        bool sawSeparator = false;
        for (int i = 2; i < span.Length; i++)
        {
            char current = span[i];
            if (current == TOKEN_CLOSE)
            {
                return sawSeparator ? i : -1;
            }

            if (current == FIELD_SEPARATOR)
            {
                sawSeparator = true;
            }
            else if (!char.IsAsciiDigit(current))
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// A token's text ends where the next readable token header begins, so parenthesised text that
    /// is not a full <c>(start,duration,flag)</c> header stays part of the text.
    /// </summary>
    private static int FindNextTokenStart(ReadOnlySpan<char> span, int from)
    {
        for (int i = from; i < span.Length; i++)
        {
            if (span[i] == TOKEN_OPEN && TryReadToken(span[i..], out _, out _, out _))
            {
                return i;
            }
        }

        return span.Length;
    }

    /// <summary>
    /// Builds the fallback text of a malformed line by removing token-shaped fragments (digits and
    /// commas in parentheses), and reports the start of the first token that does parse.
    /// </summary>
    private static string StripTokenHeaders(ReadOnlySpan<char> body, out TimeSpan? firstTokenStart)
    {
        firstTokenStart = null;
        StringBuilder builder = new(body.Length);
        int index = 0;

        while (index < body.Length)
        {
            int close = FindTokenClose(body[index..]);
            if (close >= 0)
            {
                if (firstTokenStart is null &&
                    TryReadToken(body[index..], out TimeSpan start, out _, out _))
                {
                    firstTokenStart = start;
                }

                index += close + 1;
                continue;
            }

            builder.Append(body[index]);
            index++;
        }

        return builder.ToString().Trim();
    }
}
