using Cantus.Core.Models;

namespace Cantus.Infrastructure.CoverArt;

/// <summary>
/// A source of covers that <see cref="CachedCoverArtService"/> can ask. Implementations must
/// classify their result so a miss is only cached when the catalogue really answered.
/// </summary>
public interface ICoverArtCatalog
{
    /// <summary>Short name for logs.</summary>
    string CatalogName { get; }

    Task<CoverArtFetchResult> FetchCoverArtAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
