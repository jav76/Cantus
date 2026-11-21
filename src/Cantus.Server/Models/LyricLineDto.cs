namespace Cantus.Server.Models;

public sealed record LyricLineDto
{
    public long TimestampMs { get; init; }
    public required string Text { get; init; }

    /// <summary>Real per-word timing, or null when the line only has line-level timing.</summary>
    public IReadOnlyList<LyricSyllableDto>? Syllables { get; init; }
}

public sealed record LyricSyllableDto
{
    public long TimestampMs { get; init; }
    public long DurationMs { get; init; }
    public required string Text { get; init; }
}
