using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ILyricsCacheRepository
{
    Task<SyncedLyrics?> GetCachedLyricsAsync(string trackId, CancellationToken cancellationToken = default);
    Task<bool> IsMarkedNotFoundAsync(string trackId, CancellationToken cancellationToken = default);
    Task SaveLyricsAsync(
        SyncedLyrics lyrics,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default);
    Task MarkNotFoundAsync(
        string trackId,
        string trackName,
        string artistName,
        string albumName,
        int durationMs,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The owner's override for a track, or null when the track uses automatic
    /// matching. Overrides never expire and cache writes never touch them.
    /// </summary>
    Task<LyricsOverride?> GetLyricsOverrideAsync(string trackId, CancellationToken cancellationToken = default);

    Task SaveLyricsOverrideAsync(LyricsOverride lyricsOverride, CancellationToken cancellationToken = default);

    Task RemoveLyricsOverrideAsync(string trackId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an attempt to upgrade a cached entry to word-level timing when the retry policy allows
    /// one now: the entry exists and is not a negative entry, fewer than <paramref name="maxAttempts"/>
    /// upgrade attempts were made since it was last saved, and <paramref name="retryInterval"/> has
    /// passed since it was saved or last checked. Returns false (and records nothing) otherwise.
    /// </summary>
    Task<bool> TryStartWordTimingUpgradeAsync(
        string trackId,
        int maxAttempts,
        TimeSpan retryInterval,
        CancellationToken cancellationToken = default);

    Task<int> GetTrackOffsetAsync(string trackId, CancellationToken cancellationToken = default);

    Task SetTrackOffsetAsync(string trackId, int offsetMs, CancellationToken cancellationToken = default);
}
