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

    /// <summary>
    /// Lines with syllable timing as JSON, set only when the lyrics carry word timing. LRC cannot hold
    /// syllable durations, millisecond times or a word's trailing space, so this is the source of truth
    /// for word-timed lyrics and <see cref="RawSyncedLrc"/> is only a fallback.
    /// </summary>
    public string? WordTimedLinesJson { get; set; }
    public bool IsSynced { get; set; }
    public bool IsInstrumental { get; set; }
    public bool IsNotFound { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset LastAccessedAtUtc { get; set; }
    public int WordTimingUpgradeAttempts { get; set; }
    public DateTimeOffset? WordTimingCheckedAtUtc { get; set; }
}
