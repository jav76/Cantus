namespace Cantus.Core.Search;

/// <summary>
/// Optimal string alignment distance (Levenshtein plus adjacent transposition,
/// each substring edited at most once), with the fragment matcher's length guards.
/// </summary>
internal static class OsaDistance
{
    /// <summary>
    /// True when the words are within the fuzzy allowance: one edit when the
    /// shorter word has at least <see cref="LyricMatchConstants.FUZZY_ONE_EDIT_MIN_LENGTH"/>
    /// characters, two when it has at least <see cref="LyricMatchConstants.FUZZY_TWO_EDITS_MIN_LENGTH"/>,
    /// and the length difference within that allowance.
    /// </summary>
    public static bool IsFuzzyMatch(string a, string b)
    {
        int shorter = Math.Min(a.Length, b.Length);
        int allowance = shorter >= LyricMatchConstants.FUZZY_TWO_EDITS_MIN_LENGTH
            ? 2
            : shorter >= LyricMatchConstants.FUZZY_ONE_EDIT_MIN_LENGTH ? 1 : 0;

        if (allowance == 0 || Math.Abs(a.Length - b.Length) > allowance)
        {
            return false;
        }

        return IsWithin(a, b, allowance);
    }

    /// <summary>True when the OSA distance of the two strings is at most <paramref name="maxDistance"/>.</summary>
    public static bool IsWithin(string a, string b, int maxDistance)
    {
        if (Math.Abs(a.Length - b.Length) > maxDistance)
        {
            return false;
        }

        int columns = b.Length + 1;
        int[] beforePrevious = new int[columns];
        int[] previous = new int[columns];
        int[] current = new int[columns];

        for (int j = 0; j < columns; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowMin = current[0];
            for (int j = 1; j < columns; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int value = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, beforePrevious[j - 2] + 1);
                }

                current[j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > maxDistance)
            {
                return false;
            }

            (beforePrevious, previous, current) = (previous, current, beforePrevious);
        }

        return previous[b.Length] <= maxDistance;
    }
}
