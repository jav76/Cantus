namespace Cantus.Server.Models;

public sealed record LyricsDto
{
    public required string TrackId { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Album { get; init; }
    public bool IsSynced { get; init; }
    public bool IsInstrumental { get; init; }
    public IReadOnlyList<LyricLineDto> Lines { get; init; } = [];
    public string? PlainLyrics { get; init; }
}
