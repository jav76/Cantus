using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

public interface ISpotifyPlayerClient
{
    Task<PlaybackState?> GetCurrentPlaybackAsync(string accessToken, CancellationToken cancellationToken = default);
}
