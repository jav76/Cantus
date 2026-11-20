namespace Cantus.Server.Models;

public sealed record LyricsCandidatesDto
{
    public required string TrackId { get; init; }
    public IReadOnlyList<LyricsCandidateDto> Candidates { get; init; } = [];

    /// <summary>Providers that could not be reached; their candidates are missing from the list.</summary>
    public IReadOnlyList<string> UnavailableProviders { get; init; } = [];
}
