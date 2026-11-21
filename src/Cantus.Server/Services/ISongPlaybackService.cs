using Cantus.Core.Logging;

namespace Cantus.Server.Services;

/// <summary>Plays or queues a song lookup guess on the owner's active Spotify device.</summary>
[TraceLog]
public interface ISongPlaybackService
{
    /// <summary>
    /// Plays or queues <paramref name="request"/>. Spotify trouble never throws; it
    /// is reported in the result. Nothing is sent to Spotify while its 429 back-off is active.
    /// </summary>
    /// <param name="ownerUserId">The owner's Spotify user id, whose playback is polled after a play.</param>
    /// <param name="accessToken">The owner's Spotify access token.</param>
    /// <param name="request">The guess and the action.</param>
    /// <param name="cancellationToken">Cancels the Spotify calls.</param>
    /// <exception cref="ArgumentException">
    /// The track id is malformed, or there is neither a track id nor a title and artist.
    /// </exception>
    Task<SongPlayResult> PlayAsync(
        string ownerUserId,
        [Redact] string accessToken,
        SongPlayRequest request,
        CancellationToken cancellationToken = default);
}
