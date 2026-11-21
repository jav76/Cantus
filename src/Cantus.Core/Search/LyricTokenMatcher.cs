namespace Cantus.Core.Search;

/// <summary>A token with the derived forms the matcher compares.</summary>
internal readonly record struct TokenFeatures(string Term, string Stem, string? Phonetic, bool IsCjk)
{
    public static TokenFeatures From(string term)
    {
        bool isCjk = LyricTextNormalizer.IsCjkToken(term);
        return new TokenFeatures(
            term,
            isCjk ? term : LightStemmer.Stem(term),
            isCjk ? null : PhoneticKey.Compute(term),
            isCjk);
    }
}

/// <summary>
/// Credit for aligning two tokens: exact, then light stem, OSA edit distance
/// and phonetic key, taking the best that applies. CJK tokens match exactly only.
/// </summary>
internal static class LyricTokenMatcher
{
    /// <summary>The credit, or 0 when the tokens do not match.</summary>
    public static double Score(in TokenFeatures a, in TokenFeatures b)
    {
        if (string.Equals(a.Term, b.Term, StringComparison.Ordinal))
        {
            return LyricMatchConstants.MATCH_EXACT;
        }

        if (a.IsCjk || b.IsCjk)
        {
            return 0;
        }

        if (a.Stem.Length >= LyricMatchConstants.MIN_STEM_LENGTH
            && string.Equals(a.Stem, b.Stem, StringComparison.Ordinal))
        {
            return LyricMatchConstants.MATCH_STEM;
        }

        if (OsaDistance.IsFuzzyMatch(a.Term, b.Term))
        {
            return LyricMatchConstants.MATCH_FUZZY;
        }

        if (a.Phonetic is not null && string.Equals(a.Phonetic, b.Phonetic, StringComparison.Ordinal))
        {
            return LyricMatchConstants.MATCH_PHONETIC;
        }

        return 0;
    }
}
