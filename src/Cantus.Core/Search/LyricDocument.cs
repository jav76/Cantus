namespace Cantus.Core.Search;

/// <summary>
/// Precomputed, scorer-ready lyrics: token ids from a <see cref="LyricTermTable"/>
/// (or a scorer's own ids for external text) with the original lines.
/// </summary>
/// <param name="Tokens">Token ids in reading order, one flat stream across lines.</param>
/// <param name="LineStarts">Index into <paramref name="Tokens"/> where each line starts.</param>
/// <param name="Lines">The original lines matching <paramref name="LineStarts"/>.</param>
public sealed record LyricDocument(int[] Tokens, int[] LineStarts, string[] Lines)
{
    /// <summary>
    /// The original lines covering a matched window, at most
    /// <see cref="LyricMatchConstants.MAX_MATCHED_LINES"/> starting at the window's first line.
    /// </summary>
    public IReadOnlyList<string> GetMatchedLines(LyricMatch match)
    {
        if (!match.HasWindow || LineStarts.Length == 0)
        {
            return [];
        }

        int first = LineIndexOf(match.WindowStart);
        int last = LineIndexOf(match.WindowEnd);
        int count = Math.Min(last - first + 1, LyricMatchConstants.MAX_MATCHED_LINES);
        return Lines[first..(first + count)];
    }

    private int LineIndexOf(int tokenIndex)
    {
        int found = Array.BinarySearch(LineStarts, tokenIndex);
        int index = found >= 0 ? found : ~found - 1;
        return Math.Clamp(index, 0, LineStarts.Length - 1);
    }
}
