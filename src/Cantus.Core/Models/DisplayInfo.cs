namespace Cantus.Core.Models;

/// <summary>
/// A screen paired to an owner's Session. It holds a view-only Display token, which is
/// never part of this record.
/// </summary>
public sealed record DisplayInfo
{
    public const int MAX_NAME_LENGTH = 64;

    public required string Id { get; init; }
    public required string OwnerSessionId { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastSeenAtUtc { get; init; }

    public override string ToString() =>
        $"DisplayInfo {{ Id = {Id}, OwnerSessionId = [REDACTED], Name = {Name}, LastSeenAtUtc = {LastSeenAtUtc} }}";
}

/// <summary>
/// The result of creating a Display. <see cref="Token"/> is the only copy of the plain
/// Display token; the server keeps a hash of it.
/// </summary>
public sealed record CreatedDisplay(DisplayInfo Display, string Token)
{
    public override string ToString() => $"CreatedDisplay {{ Display = {Display}, Token = [REDACTED] }}";
}
