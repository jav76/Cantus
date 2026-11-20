using Cantus.Core.Models;

namespace Cantus.Infrastructure.Lyrics;

internal static class LyricsPreview
{
    public const int MAX_LINES = 2;

    public static IReadOnlyList<string> FromLines(IEnumerable<LyricLine> lines)
    {
        return Take(lines.Select(l => l.Text));
    }

    public static IReadOnlyList<string> FromPlain(string? plainLyrics)
    {
        if (string.IsNullOrWhiteSpace(plainLyrics))
        {
            return [];
        }

        return Take(plainLyrics.Split('\n'));
    }

    private static IReadOnlyList<string> Take(IEnumerable<string> lines)
    {
        return lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(MAX_LINES)
            .ToList();
    }
}
