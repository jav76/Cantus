namespace Cantus.Server.Models;

public sealed record LyricLineDto
{
    public long TimestampMs { get; init; }
    public required string Text { get; init; }
}
