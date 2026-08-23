using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

public interface ILyricsProvider
{
    Task<SyncedLyrics?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
