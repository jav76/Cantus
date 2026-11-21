namespace Cantus.Core.Search;

public enum LyricFragmentStatus
{
    Valid = 0,

    /// <summary>The fragment is missing or has no word characters.</summary>
    Empty = 1,

    /// <summary>Fewer than three words or four CJK characters.</summary>
    TooShort = 2,

    /// <summary>More than <see cref="LyricMatchConstants.MAX_FRAGMENT_CHARACTERS"/> characters before normalization.</summary>
    TooLong = 3
}

/// <summary>
/// A remembered lyric fragment after normalization and the per-script length limits.
/// </summary>
/// <param name="Status">Whether the fragment can be searched.</param>
/// <param name="Text">The normalized (possibly truncated) fragment; empty unless valid.</param>
/// <param name="WordTokenCount">Non-CJK tokens kept.</param>
/// <param name="CjkTokenCount">CJK, kana and Hangul characters kept.</param>
/// <param name="WasTruncated">True when tokens past the cap were dropped.</param>
public sealed record LyricFragment(
    LyricFragmentStatus Status,
    NormalizedLyricText Text,
    int WordTokenCount,
    int CjkTokenCount,
    bool WasTruncated)
{
    public bool IsValid => Status == LyricFragmentStatus.Valid;

    public IReadOnlyList<string> Tokens => Text.Tokens;

    /// <summary>The normalized tokens as one search string (CJK characters are not space-separated).</summary>
    public string ToSearchText() => LyricTextNormalizer.JoinTokens(Text.Tokens);
}
