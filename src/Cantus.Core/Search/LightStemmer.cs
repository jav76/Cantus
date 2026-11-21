namespace Cantus.Core.Search;

/// <summary>
/// A deliberately light English suffix stripper. It strips the first matching
/// suffix of "ingly, edly, ings, ing, in, ies, ied, es, ed, ly, s" as long as
/// at least <see cref="LyricMatchConstants.MIN_STEM_LENGTH"/> characters stay,
/// then undoubles a final consonant ("running" to "run"). When no suffix
/// applies, a final "e" is dropped from words of five or more characters.
/// </summary>
internal static class LightStemmer
{
    private static readonly string[] Suffixes =
    [
        "ingly",
        "edly",
        "ings",
        "ing",
        "in",
        "ies",
        "ied",
        "es",
        "ed",
        "ly",
        "s",
    ];

    public static string Stem(string word)
    {
        foreach (string suffix in Suffixes)
        {
            if (!word.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            if (suffix == "in" && word.Length < LyricMatchConstants.MIN_LENGTH_FOR_IN_SUFFIX)
            {
                continue;
            }

            string stem = word[..^suffix.Length];
            if (suffix is "ies" or "ied")
            {
                stem += "y";
            }

            if (stem.Length < LyricMatchConstants.MIN_STEM_LENGTH)
            {
                continue;
            }

            return Undouble(stem);
        }

        if (word.Length >= LyricMatchConstants.MIN_LENGTH_FOR_FINAL_E_DROP && word.EndsWith('e'))
        {
            return word[..^1];
        }

        return word;
    }

    private static string Undouble(string stem)
    {
        if (stem.Length <= LyricMatchConstants.MIN_STEM_LENGTH)
        {
            return stem;
        }

        char last = stem[^1];
        if (last == stem[^2] && char.IsLetter(last) && !IsVowel(last))
        {
            return stem[..^1];
        }

        return stem;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';
}
