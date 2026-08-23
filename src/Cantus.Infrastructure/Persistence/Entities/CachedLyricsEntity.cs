namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class CachedLyricsEntity
{
    public required string TrackId { get; set; }
    public required string TrackName { get; set; }
    public required string ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public int DurationMs { get; set; }
    public string? PlainLyrics { get; set; }
    public string? RawSyncedLrc { get; set; }
    public bool IsSynced { get; set; }
    public bool IsInstrumental { get; set; }
    public bool IsNotFound { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset LastAccessedAtUtc { get; set; }
}
