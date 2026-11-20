namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class DisplayEntity
{
    public const int TOKEN_HASH_LENGTH = 64;

    public required string Id { get; set; }
    public required string OwnerSessionId { get; set; }
    public required string Name { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
}
