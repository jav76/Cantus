using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

public sealed class CachedLyricsService : ILyricsProvider
{
    private readonly ILyricsCacheRepository _cacheRepository;
    private readonly IReadOnlyList<ILyricsFetchProvider> _providers;
    private readonly LyricsCacheOptions _options;
    private readonly ILogger<CachedLyricsService> _logger;

    public CachedLyricsService(
        ILyricsCacheRepository cacheRepository,
        IReadOnlyList<ILyricsFetchProvider> providers,
        IOptions<LyricsCacheOptions> options,
        ILogger<CachedLyricsService> logger)
    {
        _cacheRepository = cacheRepository;
        _providers = providers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncedLyrics?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        // 0. The owner's override wins over every cache entry and provider
        LyricsOverride? lyricsOverride = await _cacheRepository.GetLyricsOverrideAsync(track.Id, cancellationToken);
        if (lyricsOverride is not null)
        {
            _logger.LogDebug(
                "Lyrics override {Kind} applies to track {TrackId} ({Artist} - {Title})",
                lyricsOverride.Kind,
                track.Id,
                track.Artist,
                track.Title);
            return lyricsOverride.Lyrics;
        }

        // 1. Check negative cache
        if (await _cacheRepository.IsMarkedNotFoundAsync(track.Id, cancellationToken))
        {
            _logger.LogDebug(
                "Negative cache hit for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return null;
        }

        // 2. Check SQLite positive cache
        SyncedLyrics? cached = await _cacheRepository.GetCachedLyricsAsync(track.Id, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug(
                "Cache hit for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return cached;
        }

        // 3. Query the provider chain in order until one finds lyrics
        _logger.LogInformation(
            "Cache miss for track {TrackId} ({Artist} - {Title}). Querying {ProviderCount} lyrics providers...",
            track.Id,
            track.Artist,
            track.Title,
            _providers.Count);

        bool allProvidersDefinitive = true;

        foreach (ILyricsFetchProvider provider in _providers)
        {
            LyricsFetchResult fetchResult;
            try
            {
                fetchResult = await provider.FetchLyricsAsync(track, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A provider must never take down the chain: treat an
                // unexpected throw as that provider being unavailable.
                _logger.LogWarning(
                    ex,
                    "Lyrics provider {Provider} threw for track {TrackId}; treating as unavailable.",
                    provider.ProviderName,
                    track.Id);
                allProvidersDefinitive = false;
                continue;
            }

            if (fetchResult.Lyrics is not null)
            {
                _logger.LogInformation(
                    "Lyrics for track {TrackId} ({Artist} - {Title}) found via {Provider}",
                    track.Id,
                    track.Artist,
                    track.Title,
                    provider.ProviderName);
                await _cacheRepository.SaveLyricsAsync(fetchResult.Lyrics, cancellationToken: cancellationToken);
                return fetchResult.Lyrics;
            }

            if (!fetchResult.IsDefinitive)
            {
                allProvidersDefinitive = false;
            }
        }

        // A transient failure anywhere in the chain must not be
        // negative-cached: a provider that was unreachable might have the
        // lyrics, and the entry would mark the track "not found" for the full
        // TTL. Leaving the cache untouched lets the next poll retry.
        if (!allProvidersDefinitive)
        {
            _logger.LogWarning(
                "One or more lyrics providers were unavailable for track {TrackId} ({Artist} - {Title}); skipping negative cache so it can be retried.",
                track.Id,
                track.Artist,
                track.Title);
            return null;
        }

        // 4. Mark not found with TTL
        TimeSpan ttl = TimeSpan.FromDays(_options.NegativeCacheDays);
        await _cacheRepository.MarkNotFoundAsync(
            track.Id,
            track.Title,
            track.Artist,
            track.Album ?? string.Empty,
            (int)track.Duration.TotalMilliseconds,
            ttl,
            cancellationToken);

        return null;
    }
}
