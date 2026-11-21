namespace Cantus.Server.Models;

/// <summary>
/// The override in effect for a track. <see cref="Kind"/> uses the same names
/// as the override request actions. <see cref="Provider"/> and
/// <see cref="CandidateId"/> are null for <see cref="KIND_NO_LYRICS"/>.
/// </summary>
public sealed record LyricsActiveOverrideDto
{
    public const string KIND_CANDIDATE = LyricsOverrideRequestDto.ACTION_CANDIDATE;
    public const string KIND_NO_LYRICS = LyricsOverrideRequestDto.ACTION_NO_LYRICS;

    public required string Kind { get; init; }
    public string? Provider { get; init; }
    public string? CandidateId { get; init; }
}
