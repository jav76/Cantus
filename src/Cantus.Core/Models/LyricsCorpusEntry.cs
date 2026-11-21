namespace Cantus.Core.Models;

/// <summary>
/// The effective, searchable lyrics of one track: the owner's override when one
/// exists, otherwise the positive cache row.
/// </summary>
/// <param name="TrackId">Spotify track id.</param>
/// <param name="Title">Track title (override name, falling back to the cache name).</param>
/// <param name="Artist">Artist names (override name, falling back to the cache name).</param>
/// <param name="Album">Album name, when known.</param>
/// <param name="PlainLyrics">The unsynced lyric text.</param>
/// <param name="ExpiresAtUtc">When the cache row expires; null for overrides and rows without a TTL.</param>
public sealed record LyricsCorpusEntry(
    string TrackId,
    string Title,
    string Artist,
    string? Album,
    string PlainLyrics,
    DateTimeOffset? ExpiresAtUtc);
