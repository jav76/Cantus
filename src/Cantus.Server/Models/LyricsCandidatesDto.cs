namespace Cantus.Server.Models;

public sealed record LyricsCandidatesDto
{
    public required string TrackId { get; init; }
    public IReadOnlyList<LyricsCandidateDto> Candidates { get; init; } = [];

    /// <summary>Providers that could not be reached; their candidates are missing from the list.</summary>
    public IReadOnlyList<string> UnavailableProviders { get; init; } = [];

    /// <summary>The owner's override for the track, or null when it uses automatic matching.</summary>
    public LyricsActiveOverrideDto? ActiveOverride { get; init; }
}
