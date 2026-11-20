namespace Cantus.Server.Models;

public sealed record LyricsCandidateDto
{
    public required string Provider { get; init; }
    public required string ProviderCandidateId { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Album { get; init; }
    public long DurationMs { get; init; }
    public bool IsSynced { get; init; }
    public bool IsInstrumental { get; init; }
    public IReadOnlyList<string> Preview { get; init; } = [];
}
