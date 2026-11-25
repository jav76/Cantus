using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.CoverArt;

public sealed class CachedCoverArtService : ICoverArtService
{
    private readonly ICoverArtCacheRepository _cache;
    private readonly ICoverArtCatalog _catalog;
    private readonly CoverArtOptions _options;
    private readonly ILogger<CachedCoverArtService> _logger;

    public CachedCoverArtService(
        ICoverArtCacheRepository cache,
        ICoverArtCatalog catalog,
        IOptions<CoverArtOptions> options,
        ILogger<CachedCoverArtService> logger)
    {
        _cache = cache;
        _catalog = catalog;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CoverArtLookup> GetCachedAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        try
        {
            CoverArtCacheEntry? entry = await _cache.GetAsync(track.Id, cancellationToken);
            if (entry is null)
            {
                return CoverArtLookup.Unresolved();
            }

            return entry.ArtUrl is { } artUrl ? CoverArtLookup.Found(artUrl) : CoverArtLookup.NotFound();
        }
        catch (Exception ex) when (IsNotCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Could not read the cover art cache for track {TrackId}", track.Id);
            return CoverArtLookup.Unresolved();
        }
    }

    public async Task<CoverArtLookup> ResolveAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        CoverArtLookup cached = await GetCachedAsync(track, cancellationToken);
        if (cached.Status != CoverArtLookupStatus.Unresolved || !_options.Enabled)
        {
            return cached;
        }

        CoverArtFetchResult result = await FetchAsync(track, cancellationToken);
        if (result.ArtUrl is { } artUrl)
        {
            _logger.LogInformation(
                "Cover art for track {TrackId} ({Artist} - {Title}) found via {Catalog}",
                track.Id,
                track.Artist,
                track.Title,
                _catalog.CatalogName);
            await SaveAsync(track, artUrl, TimeSpan.FromDays(_options.CacheDays), cancellationToken);
            return CoverArtLookup.Found(artUrl);
        }

        // An unreachable catalogue might have the cover. Caching that as a miss would hide it
        // for the whole negative-cache period.
        if (!result.IsDefinitive)
        {
            return CoverArtLookup.Unresolved();
        }

        _logger.LogDebug(
            "{Catalog} has no confident cover art match for track {TrackId} ({Artist} - {Title})",
            _catalog.CatalogName,
            track.Id,
            track.Artist,
            track.Title);
        await SaveAsync(track, null, TimeSpan.FromDays(_options.NegativeCacheDays), cancellationToken);
        return CoverArtLookup.NotFound();
    }

    private async Task<CoverArtFetchResult> FetchAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        try
        {
            return await _catalog.FetchCoverArtAsync(track, cancellationToken);
        }
        catch (Exception ex) when (IsNotCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "Cover art catalogue {Catalog} threw for track {TrackId}; treating it as unavailable.",
                _catalog.CatalogName,
                track.Id);
            return CoverArtFetchResult.Unavailable();
        }
    }

    /// <summary>A failed write loses only the cache entry: the answer is still returned.</summary>
    private async Task SaveAsync(TrackInfo track, string? artUrl, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SaveAsync(track.Id, artUrl, timeToLive, cancellationToken);
        }
        catch (Exception ex) when (IsNotCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Could not cache the cover art answer for track {TrackId}", track.Id);
        }
    }

    /// <summary>An HttpClient timeout is an OperationCanceledException too, and must not escape as one.</summary>
    internal static bool IsNotCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
}
