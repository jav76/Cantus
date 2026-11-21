namespace Cantus.Core.Search;

/// <summary>
/// Picks contiguous fragment windows that might be the song's title, for
/// providers that search by title only. Windows are 2 to 4 tokens and not made
/// only of stopwords. A window at the start of the fragment and one with rarer
/// words ranks higher; ties prefer longer, then earlier windows. A window that
/// overlaps an already chosen one is skipped.
/// </summary>
public static class TitleGuessGenerator
{
    public const int MIN_WINDOW_TOKENS = 2;
    public const int MAX_WINDOW_TOKENS = 4;

    private const double START_OF_FRAGMENT_BONUS = 0.5;
    private const double CONTENT_WORD_WEIGHT = 1.0;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "do", "for", "from", "i", "im", "if", "in",
        "is", "it", "its", "me", "my", "no", "not", "of", "oh", "on", "or", "so", "that", "the", "this",
        "to", "up", "we", "with", "you", "your", "yeah", "ooh", "ah", "la", "na", "hey", "all", "just",
    };

    /// <summary>
    /// Up to <paramref name="maxGuesses"/> title guesses, best first. When
    /// <paramref name="terms"/> holds enough documents, rarity comes from its idf;
    /// otherwise every non-stopword counts the same.
    /// </summary>
    public static IReadOnlyList<string> Generate(
        IReadOnlyList<string> fragmentTokens,
        int maxGuesses,
        LyricTermTable? terms = null)
    {
        if (maxGuesses <= 0 || fragmentTokens.Count < MIN_WINDOW_TOKENS)
        {
            return [];
        }

        double[] rarity = ComputeRarity(fragmentTokens, terms);
        List<(int Start, int Length, double Score)> windows = new();

        for (int length = MIN_WINDOW_TOKENS; length <= MAX_WINDOW_TOKENS; length++)
        {
            for (int start = 0; start + length <= fragmentTokens.Count; start++)
            {
                double contentScore = 0;
                for (int i = start; i < start + length; i++)
                {
                    contentScore += rarity[i];
                }

                if (contentScore <= 0)
                {
                    continue;
                }

                double score = CONTENT_WORD_WEIGHT * contentScore / length
                    + (start == 0 ? START_OF_FRAGMENT_BONUS : 0);
                windows.Add((start, length, score));
            }
        }

        List<(int Start, int Length)> chosen = new();
        List<string> guesses = new();
        foreach ((int start, int length, double _) in windows
            .OrderByDescending(w => w.Score)
            .ThenByDescending(w => w.Length)
            .ThenBy(w => w.Start))
        {
            if (chosen.Any(c => start < c.Start + c.Length && c.Start < start + length))
            {
                continue;
            }

            string guess = LyricTextNormalizer.JoinTokens(fragmentTokens.Skip(start).Take(length).ToList());
            if (guesses.Contains(guess, StringComparer.Ordinal))
            {
                continue;
            }

            chosen.Add((start, length));
            guesses.Add(guess);
            if (guesses.Count == maxGuesses)
            {
                break;
            }
        }

        return guesses;
    }

    /// <summary>0 for stopwords; otherwise 1, or the token's idf relative to the most informative fragment word.</summary>
    private static double[] ComputeRarity(IReadOnlyList<string> tokens, LyricTermTable? terms)
    {
        double[] rarity = new double[tokens.Count];
        bool useIdf = terms is not null && terms.DocumentCount >= LyricMatchConstants.IDF_MIN_DOCUMENTS;

        for (int i = 0; i < tokens.Count; i++)
        {
            if (Stopwords.Contains(tokens[i]))
            {
                continue;
            }

            if (!useIdf)
            {
                rarity[i] = 1;
                continue;
            }

            int documentFrequency = terms!.TryGetTermId(tokens[i], out int id) ? terms.GetDocumentFrequency(id) : 0;
            double smoothing = LyricMatchConstants.BM25_IDF_SMOOTHING;
            rarity[i] = Math.Log(1 + (terms.DocumentCount - documentFrequency + smoothing) / (documentFrequency + smoothing));
        }

        double max = rarity.Length > 0 ? rarity.Max() : 0;
        if (max > 0)
        {
            for (int i = 0; i < rarity.Length; i++)
            {
                rarity[i] /= max;
            }
        }

        return rarity;
    }
}
