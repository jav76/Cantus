namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class TrackOffsetEntity
{
    public required string TrackId { get; set; }
    public int OffsetMs { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
