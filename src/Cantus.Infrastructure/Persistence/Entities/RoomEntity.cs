namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class RoomEntity
{
    public required string RoomCode { get; set; }
    public required string HostUserId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
