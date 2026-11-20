using Cantus.Core.Models;

namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class LyricsOverrideEntity
{
    public required string TrackId { get; set; }
    public required LyricsOverrideKind Kind { get; set; }
    public string? Provider { get; set; }
    public string? ProviderCandidateId { get; set; }
    public string? TrackName { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public string? PlainLyrics { get; set; }
    public string? RawSyncedLrc { get; set; }
    public bool IsSynced { get; set; }
    public bool IsInstrumental { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
