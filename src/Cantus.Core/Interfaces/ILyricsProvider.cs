using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ILyricsProvider
{
    Task<SyncedLyrics?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
