namespace Cantus.Core.Search;

/// <summary>
/// Whole-word comparisons between names already normalized by <see cref="LyricTextNormalizer"/>,
/// shared by the matchers that decide whether a catalogue entry is the track being looked for.
/// </summary>
public static class TrackNameMatch
{
    private const char WORD_BOUNDARY = ' ';

    /// <summary>True when <paramref name="needle"/> appears in <paramref name="haystack"/> on word boundaries.</summary>
    public static bool ContainsWords(string haystack, string needle)
    {
        if (needle.Length == 0 || haystack.Length == 0)
        {
            return false;
        }

        return (WORD_BOUNDARY + haystack + WORD_BOUNDARY).Contains(
            WORD_BOUNDARY + needle + WORD_BOUNDARY,
            StringComparison.Ordinal);
    }

    /// <summary>True when either name contains the other on word boundaries.</summary>
    public static bool EitherContainsWords(string first, string second) =>
        ContainsWords(first, second) || ContainsWords(second, first);

    /// <summary>True when any artist key of one side matches any of the other.</summary>
    public static bool AnyArtistMatches(IReadOnlyList<string> guessArtists, IReadOnlyList<string> trackArtists)
    {
        foreach (string guessArtist in guessArtists)
        {
            foreach (string trackArtist in trackArtists)
            {
                if (EitherContainsWords(trackArtist, guessArtist))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
