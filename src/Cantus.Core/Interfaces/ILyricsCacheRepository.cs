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

    Task<int> GetTrackOffsetAsync(string trackId, CancellationToken cancellationToken = default);

    Task SetTrackOffsetAsync(string trackId, int offsetMs, CancellationToken cancellationToken = default);
}
