using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

/// <summary>Searches the Spotify catalog on behalf of a signed-in owner.</summary>
[TraceLog]
public interface ISpotifyCatalogClient
{
    /// <summary>
    /// Searches tracks in the market of the token's account, in Spotify's relevance order.
    /// An empty list means Spotify found nothing.
    /// </summary>
    /// <param name="accessToken">The owner's Spotify access token.</param>
    /// <param name="query">A Spotify search query, field filters such as <c>track:"..."</c> allowed.</param>
    /// <param name="limit">The most tracks to return.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="SpotifyRateLimitedException">
    /// Spotify calls are paused by a recent 429, or this search was answered with one.
    /// </exception>
    Task<IReadOnlyList<TrackInfo>> SearchTracksAsync(
        [Redact] string accessToken,
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}
