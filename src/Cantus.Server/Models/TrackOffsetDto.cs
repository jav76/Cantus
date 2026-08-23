namespace Cantus.Server.Models;

public sealed record TrackOffsetDto
{
    public required string TrackId { get; init; }
    public int OffsetMs { get; init; }
}
