namespace Cantus.Core.Search;

/// <summary>
/// How well a fragment matches one lyric text. <see cref="WindowStart"/> and
/// <see cref="WindowEnd"/> are the inclusive token range of the best alignment,
/// or -1 when nothing aligned.
/// </summary>
public readonly record struct LyricMatch(
    double Score,
    double Align,
    double Dice,
    double IdfBagOfWords,
    int WindowStart,
    int WindowEnd)
{
    public static LyricMatch None { get; } = new(0, 0, 0, 0, -1, -1);

    public bool HasWindow => WindowStart >= 0 && WindowEnd >= WindowStart;
}
