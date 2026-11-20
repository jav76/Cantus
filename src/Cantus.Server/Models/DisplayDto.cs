namespace Cantus.Server.Models;

/// <summary>
/// A Display as its owner sees it. Carries neither the Display token nor the owner's Session id.
/// </summary>
public sealed record DisplayDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastSeenAtUtc { get; init; }
}
