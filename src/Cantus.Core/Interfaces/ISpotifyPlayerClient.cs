using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ISpotifyPlayerClient
{
    Task<PlaybackState?> GetCurrentPlaybackAsync(string accessToken, CancellationToken cancellationToken = default);
}
