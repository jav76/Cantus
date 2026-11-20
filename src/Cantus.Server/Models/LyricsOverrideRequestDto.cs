namespace Cantus.Server.Models;

/// <summary>
/// The owner's lyrics correction for a track. <see cref="Provider"/> and
/// <see cref="CandidateId"/> are required only for <see cref="ACTION_CANDIDATE"/>.
/// </summary>
public sealed record LyricsOverrideRequestDto
{
    public const string ACTION_CANDIDATE = "candidate";
    public const string ACTION_REFETCH = "refetch";
    public const string ACTION_NO_LYRICS = "noLyrics";
    public const string ACTION_CLEAR = "clear";

    public required string Action { get; init; }
    public string? Provider { get; init; }
    public string? CandidateId { get; init; }
}
