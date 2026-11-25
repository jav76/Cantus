namespace Cantus.Infrastructure.CoverArt;

/// <summary>One recording from a MusicBrainz search, reduced to what cover matching reads.</summary>
/// <param name="Artists">Every credited artist, both as credited and by the artist's own name.</param>
/// <param name="Length">Null when MusicBrainz has no length for the recording.</param>
public sealed record MusicBrainzRecording(
    string Title,
    IReadOnlyList<string> Artists,
    TimeSpan? Length,
    IReadOnlyList<MusicBrainzRelease> Releases);

/// <summary>A release a recording appears on, with the release group that carries the cover.</summary>
/// <param name="Date">As MusicBrainz writes it: a year, a year and month, or a full date.</param>
public sealed record MusicBrainzRelease(
    Guid ReleaseGroupId,
    string Title,
    string? Status,
    string? PrimaryType,
    IReadOnlyList<string> SecondaryTypes,
    string? Date);
