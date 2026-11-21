using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cantus.Core.Search;

/// <summary>
/// Turns lyrics, fragments, titles and artist names into comparable tokens.
/// <list type="bullet">
/// <item>Blank and section-label lines are dropped: a whole-line <c>[...]</c> tag, or a line
/// starting with verse, chorus, bridge, intro, outro, pre-chorus, prechorus, hook, refrain or
/// interlude, an optional number and a colon, whatever follows it (<c>Verse 2:</c>,
/// <c>Chorus: Singer</c>). Inline <c>[...]</c> tags are removed; parenthesised text is kept.</item>
/// <item>NFKD, then only U+0300 to U+036F are stripped and the rest is recomposed
/// with NFKC, which folds accented Latin, full-width forms and ligatures while
/// keeping Hangul and voiced kana intact. Then lower case plus a small fold table.</item>
/// <item>Apostrophes are deleted ("don't" equals "dont"). Letters, digits and
/// remaining combining marks are word characters; everything else separates words.</item>
/// <item>Each Han, kana or Hangul character is its own token.</item>
/// </list>
/// </summary>
public static partial class LyricTextNormalizer
{
    private const int COMBINING_DIACRITICS_FIRST = 0x0300;
    private const int COMBINING_DIACRITICS_LAST = 0x036F;

    /// <summary>A single rune is at most a surrogate pair.</summary>
    private const int MAX_RUNE_UTF16_LENGTH = 2;

    private const int KATAKANA_DOUBLE_HYPHEN = 0x30A0;
    private const int KATAKANA_MIDDLE_DOT = 0x30FB;

    /// <summary>A word counts this many units toward the fragment cap; a CJK character counts one.</summary>
    private const int WORD_TOKEN_UNITS = LyricMatchConstants.MAX_CJK_TOKENS / LyricMatchConstants.MAX_WORD_TOKENS;

    private const int MAX_FRAGMENT_UNITS = LyricMatchConstants.MAX_CJK_TOKENS;
    private const int MIN_MIXED_FRAGMENT_UNITS = LyricMatchConstants.MIN_WORD_TOKENS * WORD_TOKEN_UNITS;

    private static readonly char[] LineSeparators = ['\r', '\n'];

    private static readonly Dictionary<char, string> FoldTable = new()
    {
        ['ø'] = "o",
        ['ł'] = "l",
        ['đ'] = "d",
        ['æ'] = "ae",
        ['œ'] = "oe",
        ['ß'] = "ss",
        ['ı'] = "i",
    };

    [GeneratedRegex(@"^\[[^\]]*\]$", RegexOptions.CultureInvariant)]
    private static partial Regex BracketLabelLine();

    [GeneratedRegex(
        @"^(verse|chorus|bridge|intro|outro|pre-chorus|prechorus|hook|refrain|interlude)(\s*\d+)?\s*:.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedLabelLine();

    [GeneratedRegex(@"\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex InlineBracketTag();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedWhitespace();

    [GeneratedRegex(@"[\(\[]([^\)\]]*)[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex TitleBracketSegment();

    [GeneratedRegex(@"\s+[-\u2013\u2014]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex TitleDashSeparator();

    [GeneratedRegex(
        @"\b(remaster(ed)?|live|feat\.?|ft\.?|featuring|version|edit|mix|remix|deluxe|mono|stereo|acoustic|demo|bonus|single|radio|explicit|instrumental|anniversary|\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleQualifier();

    [GeneratedRegex(
        @"\s*(?:,|&|;|/|\+|\bfeat\.?|\bft\.|\bfeaturing\b|\s+x\s+)\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArtistSeparator();

    /// <summary>Normalizes multi-line lyric text into one token stream with line starts.</summary>
    public static NormalizedLyricText Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return NormalizedLyricText.Empty;
        }

        List<string> tokens = new();
        List<int> lineStarts = new();
        List<string> lines = new();

        foreach (string rawLine in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || BracketLabelLine().IsMatch(line) || NamedLabelLine().IsMatch(line))
            {
                continue;
            }

            line = RepeatedWhitespace().Replace(InlineBracketTag().Replace(line, " "), " ").Trim();
            int start = tokens.Count;
            AppendTokens(line, tokens);
            if (tokens.Count > start)
            {
                lineStarts.Add(start);
                lines.Add(line);
            }
        }

        return new NormalizedLyricText(tokens.ToArray(), lineStarts.ToArray(), lines.ToArray());
    }

    /// <summary>Normalized tokens of a single string, with no line handling.</summary>
    public static string[] Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<string> tokens = new();
        AppendTokens(text, tokens);
        return tokens.ToArray();
    }

    /// <summary>
    /// Validates and normalizes a remembered fragment. Limits are per script:
    /// 3 to 24 words, or 4 to 48 CJK characters (a word counts as two CJK
    /// characters toward the cap). Longer input is truncated to the cap.
    /// </summary>
    public static LyricFragment PrepareFragment(string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return new LyricFragment(LyricFragmentStatus.Empty, NormalizedLyricText.Empty, 0, 0, false);
        }

        if (fragment.Length > LyricMatchConstants.MAX_FRAGMENT_CHARACTERS)
        {
            return new LyricFragment(LyricFragmentStatus.TooLong, NormalizedLyricText.Empty, 0, 0, false);
        }

        NormalizedLyricText normalized = Normalize(fragment);
        if (normalized.Tokens.Length == 0)
        {
            return new LyricFragment(LyricFragmentStatus.Empty, NormalizedLyricText.Empty, 0, 0, false);
        }

        int wordCount = 0;
        int cjkCount = 0;
        int units = 0;
        int kept = 0;
        foreach (string token in normalized.Tokens)
        {
            bool isCjk = IsCjkToken(token);
            int cost = isCjk ? 1 : WORD_TOKEN_UNITS;
            if (units + cost > MAX_FRAGMENT_UNITS)
            {
                break;
            }

            units += cost;
            kept++;
            if (isCjk)
            {
                cjkCount++;
            }
            else
            {
                wordCount++;
            }
        }

        bool tooShort = wordCount < LyricMatchConstants.MIN_WORD_TOKENS
            && cjkCount < LyricMatchConstants.MIN_CJK_TOKENS
            && units < MIN_MIXED_FRAGMENT_UNITS;
        if (tooShort)
        {
            return new LyricFragment(LyricFragmentStatus.TooShort, NormalizedLyricText.Empty, wordCount, cjkCount, false);
        }

        bool truncated = kept < normalized.Tokens.Length;
        NormalizedLyricText text = truncated ? Truncate(normalized, kept) : normalized;
        return new LyricFragment(LyricFragmentStatus.Valid, text, wordCount, cjkCount, truncated);
    }

    /// <summary>
    /// The title without remaster, live, feat, version, edit or mix qualifiers in
    /// brackets or after a spaced dash, as normalized tokens joined by spaces.
    /// </summary>
    public static string TitleCore(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        title = CapNameLength(title);
        string core = TitleBracketSegment().Replace(
            title,
            match => TitleQualifier().IsMatch(match.Groups[1].Value) ? " " : match.Value);

        string[] dashParts = TitleDashSeparator().Split(core);
        StringBuilder kept = new(dashParts[0]);
        for (int i = 1; i < dashParts.Length; i++)
        {
            if (TitleQualifier().IsMatch(dashParts[i]))
            {
                break;
            }

            kept.Append(' ').Append(dashParts[i]);
        }

        string normalized = JoinTokens(Tokenize(kept.ToString()));
        return normalized.Length > 0 ? normalized : JoinTokens(Tokenize(title));
    }

    /// <summary>The first credited artist as normalized tokens joined by spaces.</summary>
    public static string ArtistKey(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return string.Empty;
        }

        foreach (string part in ArtistSeparator().Split(CapNameLength(artist)))
        {
            string key = JoinTokens(Tokenize(part));
            if (key.Length > 0)
            {
                return key;
            }
        }

        return string.Empty;
    }

    /// <summary>Every credited artist as normalized tokens joined by spaces, in credit order.</summary>
    public static IReadOnlyList<string> ArtistKeys(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return Array.Empty<string>();
        }

        List<string> keys = new();
        foreach (string part in ArtistSeparator().Split(CapNameLength(artist)))
        {
            string key = JoinTokens(Tokenize(part));
            if (key.Length > 0 && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Keeps the name regexes linear: provider and request strings can be padded far
    /// beyond any real title or artist.
    /// </summary>
    private static string CapNameLength(string name)
    {
        if (name.Length <= LyricMatchConstants.MAX_NAME_CHARACTERS)
        {
            return name;
        }

        int length = LyricMatchConstants.MAX_NAME_CHARACTERS;
        if (char.IsHighSurrogate(name[length - 1]))
        {
            length--;
        }

        return name[..length];
    }

    /// <summary>True for a single Han, kana or Hangul character token.</summary>
    public static bool IsCjkToken(string token)
    {
        if (token.Length == 0 || token.Length > MAX_RUNE_UTF16_LENGTH)
        {
            return false;
        }

        return Rune.TryGetRuneAt(token, 0, out Rune rune)
            && rune.Utf16SequenceLength == token.Length
            && IsCjk(rune);
    }

    /// <summary>Joins tokens with spaces, except between two CJK characters.</summary>
    public static string JoinTokens(IReadOnlyList<string> tokens)
    {
        StringBuilder builder = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0 && !(IsCjkToken(tokens[i - 1]) && IsCjkToken(tokens[i])))
            {
                builder.Append(' ');
            }

            builder.Append(tokens[i]);
        }

        return builder.ToString();
    }

    private static NormalizedLyricText Truncate(NormalizedLyricText text, int tokenCount)
    {
        List<int> lineStarts = new();
        List<string> lines = new();
        for (int i = 0; i < text.LineStarts.Length && text.LineStarts[i] < tokenCount; i++)
        {
            lineStarts.Add(text.LineStarts[i]);
            lines.Add(text.Lines[i]);
        }

        return new NormalizedLyricText(text.Tokens[..tokenCount], lineStarts.ToArray(), lines.ToArray());
    }

    private static void AppendTokens(string text, List<string> tokens)
    {
        string folded = Fold(text);
        StringBuilder word = new();

        foreach (Rune rune in folded.EnumerateRunes())
        {
            if (IsCjk(rune))
            {
                Flush(word, tokens);
                tokens.Add(rune.ToString());
            }
            else if (IsWordRune(rune))
            {
                word.Append(rune.ToString());
            }
            else
            {
                Flush(word, tokens);
            }
        }

        Flush(word, tokens);
    }

    private static void Flush(StringBuilder word, List<string> tokens)
    {
        if (word.Length > 0)
        {
            tokens.Add(word.ToString());
            word.Clear();
        }
    }

    private static string Fold(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormKD);
        StringBuilder stripped = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (c < COMBINING_DIACRITICS_FIRST || c > COMBINING_DIACRITICS_LAST)
            {
                stripped.Append(c);
            }
        }

        string lowered = stripped.ToString().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        StringBuilder folded = new(lowered.Length);
        foreach (char c in lowered)
        {
            if (IsApostrophe(c))
            {
                continue;
            }

            if (FoldTable.TryGetValue(c, out string? replacement))
            {
                folded.Append(replacement);
            }
            else
            {
                folded.Append(c);
            }
        }

        return folded.ToString();
    }

    private static bool IsApostrophe(char c) => c is (char)0x0027 or (char)0x2019 or (char)0x2018 or (char)0x02BC;

    private static bool IsWordRune(Rune rune)
    {
        if (Rune.IsLetterOrDigit(rune))
        {
            return true;
        }

        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    private static bool IsCjk(Rune rune)
    {
        int value = rune.Value;
        if (value is KATAKANA_DOUBLE_HYPHEN or KATAKANA_MIDDLE_DOT)
        {
            return false;
        }

        return value is
            (>= 0x3040 and <= 0x30FF)       // Hiragana, Katakana
            or (>= 0x3005 and <= 0x3007)    // Iteration mark, closing mark, ideographic zero
            or 0x303B                       // Vertical ideographic iteration mark
            or (>= 0x31F0 and <= 0x31FF)    // Katakana phonetic extensions
            or (>= 0x3400 and <= 0x4DBF)    // CJK extension A
            or (>= 0x4E00 and <= 0x9FFF)    // CJK unified ideographs
            or (>= 0xF900 and <= 0xFAFF)    // CJK compatibility ideographs
            or (>= 0xFF66 and <= 0xFF9F)    // Half-width katakana
            or (>= 0x1100 and <= 0x11FF)    // Hangul jamo
            or (>= 0x3130 and <= 0x318F)    // Hangul compatibility jamo
            or (>= 0xAC00 and <= 0xD7AF)    // Hangul syllables
            or (>= 0x20000 and <= 0x3134F); // CJK extensions B to G
    }
}
