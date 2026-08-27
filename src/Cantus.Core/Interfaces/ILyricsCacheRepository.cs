using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

public interface ILyricsCacheRepository
{
    Task<SyncedLyrics?> GetCachedLyricsAsync(string trackId, CancellationToken cancellationToken = default);
    Task<bool> IsMarkedNotFoundAsync(string trackId, CancellationToken cancellationToken = default);
    Task SaveLyricsAsync(SyncedLyrics lyrics, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);
    Task MarkNotFoundAsync(
        string trackId,
        string trackName,
        string artistName,
        string albumName,
        int durationMs,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    Task<int> GetTrackOffsetAsync(string trackId, CancellationToken cancellationToken = default);

    Task SetTrackOffsetAsync(string trackId, int offsetMs, CancellationToken cancellationToken = default);
}
