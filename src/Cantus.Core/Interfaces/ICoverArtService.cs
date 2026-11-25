using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

/// <summary>
/// Finds a cover for a track whose player offered none, from an open catalogue, and remembers
/// the answer per Track key. Neither method throws for catalogue or cache trouble.
/// </summary>
[TraceLog]
public interface ICoverArtService
{
    /// <summary>
    /// Reads the cache only, so it is safe on the path that broadcasts playback.
    /// <see cref="CoverArtLookupStatus.Unresolved"/> means the track has no unexpired entry.
    /// </summary>
    Task<CoverArtLookup> GetCachedAsync(TrackInfo track, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached answer, or asks the catalogue and caches what it said. Slow and
    /// rate-limited: call it in the background. <see cref="CoverArtLookupStatus.Unresolved"/>
    /// means the catalogue could not be reached, and nothing was cached.
    /// </summary>
    Task<CoverArtLookup> ResolveAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
