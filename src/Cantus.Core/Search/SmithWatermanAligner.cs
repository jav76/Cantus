namespace Cantus.Core.Search;

/// <summary>Best local alignment found by <see cref="SmithWatermanAligner"/>.</summary>
internal readonly record struct AlignmentResult(double Best, int Start, int End);

/// <summary>
/// Word-level Smith-Waterman with linear gaps: fragment positions are rows,
/// lyric tokens are columns. A column whose row is null matched no fragment
/// position; after a dead column it cannot raise any cell, so it is skipped.
/// </summary>
internal static class SmithWatermanAligner
{
    /// <param name="fragmentLength">Number of fragment positions (rows).</param>
    /// <param name="columnCount">Number of lyric tokens (columns).</param>
    /// <param name="rowAt">Per-position credit for column j, or null when nothing matches.</param>
    /// <param name="mismatchRow">The row used for a non-matching column (all <c>-MISMATCH_PENALTY</c>).</param>
    /// <param name="scores">Scratch buffer of at least <paramref name="fragmentLength"/>.</param>
    /// <param name="starts">Scratch buffer of at least <paramref name="fragmentLength"/>.</param>
    public static AlignmentResult Align(
        int fragmentLength,
        int columnCount,
        Func<int, double[]?> rowAt,
        double[] mismatchRow,
        double[] scores,
        int[] starts)
    {
        Array.Clear(scores, 0, fragmentLength);
        Array.Fill(starts, -1, 0, fragmentLength);

        bool alive = false;
        double best = 0;
        int bestStart = -1;
        int bestEnd = -1;

        for (int j = 0; j < columnCount; j++)
        {
            double[]? row = rowAt(j);
            if (row is null)
            {
                if (!alive)
                {
                    continue;
                }

                row = mismatchRow;
            }

            double diagonal = 0;
            int diagonalStart = -1;
            double up = 0;
            int upStart = -1;
            double columnMax = 0;

            for (int i = 0; i < fragmentLength; i++)
            {
                double left = scores[i];
                int leftStart = starts[i];

                double value = diagonal + row[i];
                int valueStart = diagonal > 0 ? diagonalStart : j;

                double skipLyric = left - LyricMatchConstants.GAP_LYRIC_PENALTY;
                if (skipLyric > value)
                {
                    value = skipLyric;
                    valueStart = leftStart;
                }

                double skipFragment = up - LyricMatchConstants.GAP_FRAGMENT_PENALTY;
                if (skipFragment > value)
                {
                    value = skipFragment;
                    valueStart = upStart;
                }

                if (value <= 0)
                {
                    value = 0;
                    valueStart = -1;
                }

                diagonal = left;
                diagonalStart = leftStart;
                scores[i] = value;
                starts[i] = valueStart;
                up = value;
                upStart = valueStart;

                if (value > columnMax)
                {
                    columnMax = value;
                }

                if (value > best)
                {
                    best = value;
                    bestStart = valueStart;
                    bestEnd = j;
                }
            }

            alive = columnMax > 0;
        }

        return new AlignmentResult(best, bestStart, bestEnd);
    }

    /// <summary>
    /// Alignment score in 0..1 of <paramref name="fragment"/> inside <paramref name="target"/>,
    /// normalized by the fragment length. Used to compare an external excerpt with another lyric.
    /// </summary>
    public static double AlignTokens(IReadOnlyList<string> fragment, IReadOnlyList<string> target)
    {
        int n = fragment.Count;
        if (n == 0 || target.Count == 0)
        {
            return 0;
        }

        TokenFeatures[] features = fragment.Select(TokenFeatures.From).ToArray();
        double[] mismatchRow = CreateMismatchRow(n);
        Dictionary<string, double[]?> rows = new(StringComparer.Ordinal);

        double[]? RowAt(int column)
        {
            string term = target[column];
            if (!rows.TryGetValue(term, out double[]? row))
            {
                TokenFeatures lyric = TokenFeatures.From(term);
                for (int i = 0; i < n; i++)
                {
                    double credit = LyricTokenMatcher.Score(features[i], lyric);
                    if (credit > 0)
                    {
                        row ??= (double[])mismatchRow.Clone();
                        row[i] = credit;
                    }
                }

                rows[term] = row;
            }

            return row;
        }

        AlignmentResult result = Align(n, target.Count, RowAt, mismatchRow, new double[n], new int[n]);
        return Math.Min(1.0, result.Best / n);
    }

    public static double[] CreateMismatchRow(int fragmentLength)
    {
        double[] row = new double[fragmentLength];
        Array.Fill(row, -LyricMatchConstants.MISMATCH_PENALTY);
        return row;
    }
}
