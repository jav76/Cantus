namespace Cantus.Core.Search;

/// <summary>
/// Lyric text as one flat token stream, so a fragment stitched across two
/// lines still aligns. <see cref="LineStarts"/>[i] is the index of the first
/// token of <see cref="Lines"/>[i]; lines without tokens are left out.
/// </summary>
/// <param name="Tokens">Normalized tokens in reading order.</param>
/// <param name="LineStarts">Index into <paramref name="Tokens"/> where each kept line starts.</param>
/// <param name="Lines">The kept original lines, trimmed, without inline <c>[...]</c> tags.</param>
public sealed record NormalizedLyricText(string[] Tokens, int[] LineStarts, string[] Lines)
{
    public static NormalizedLyricText Empty { get; } = new([], [], []);
}
