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
            return await TryUpgradeToWordTimingAsync(track, cached, cancellationToken) ?? cached;
        }

        // 3. Query the provider chain in order, preferring a word-timed result
        _logger.LogInformation(
            "Cache miss for track {TrackId} ({Artist} - {Title}). Querying {ProviderCount} lyrics providers...",
            track.Id,
            track.Artist,
            track.Title,
            _providers.Count);

        LyricsFetchResult result = await RefreshFromProvidersAsync(track, cancellationToken);
        return result.Lyrics;
    }

    /// <summary>
    /// Queries the provider chain without consulting the override or the
    /// caches, preferring a word-timed result, and stores a definitive answer
    /// in the cache: the lyrics, or a "not found" mark when every provider
    /// answered without a match. When a provider was unreachable and none
    /// found lyrics, the cache is left as it was and the result is not
    /// definitive.
    /// </summary>
    public async Task<LyricsFetchResult> RefreshFromProvidersAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        (SyncedLyrics? found, bool allProvidersDefinitive) = await QueryProvidersAsync(track, cancellationToken);
        if (found is not null)
        {
            await _cacheRepository.SaveLyricsAsync(found, cancellationToken: cancellationToken);
            return LyricsFetchResult.Found(found);
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
            return LyricsFetchResult.Unavailable();
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

        return LyricsFetchResult.NotFound();
    }

    /// <summary>
    /// Re-queries the chain for a cached entry that has no word timing, at most
    /// <see cref="LyricsCacheOptions.WordTimingUpgradeMaxAttempts"/> times and no more often than
    /// <see cref="LyricsCacheOptions.WordTimingUpgradeIntervalDays"/>. The attempt is recorded before
    /// the fetch, so a failing provider cannot cause a refetch on every play. Returns the word-timed
    /// lyrics after saving them, or null to keep the cached entry as it is.
    /// </summary>
    private async Task<SyncedLyrics?> TryUpgradeToWordTimingAsync(
        TrackInfo track,
        SyncedLyrics cached,
        CancellationToken cancellationToken)
    {
        if (cached.IsInstrumental || cached.HasWordTiming || _options.WordTimingUpgradeMaxAttempts <= 0)
        {
            return null;
        }

        bool started = await _cacheRepository.TryStartWordTimingUpgradeAsync(
            track.Id,
            _options.WordTimingUpgradeMaxAttempts,
            TimeSpan.FromDays(_options.WordTimingUpgradeIntervalDays),
            cancellationToken);
        if (!started)
        {
            return null;
        }

        (SyncedLyrics? found, _) = await QueryProvidersAsync(track, cancellationToken);
        if (found is null || !found.HasWordTiming)
        {
            _logger.LogDebug(
                "No word-timed lyrics yet for cached track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return null;
        }

        _logger.LogInformation(
            "Upgraded cached lyrics for track {TrackId} ({Artist} - {Title}) to word timing",
            track.Id,
            track.Artist,
            track.Title);
        await _cacheRepository.SaveLyricsAsync(found, cancellationToken: cancellationToken);
        return found;
    }

    /// <summary>
    /// Walks the providers in order. The first result found is kept unless a later provider returns
    /// word timing; the walk stops at the first word-timed or instrumental result.
    /// </summary>
    private async Task<(SyncedLyrics? Found, bool AllProvidersDefinitive)> QueryProvidersAsync(
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        SyncedLyrics? found = null;
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

            if (fetchResult.Lyrics is null)
            {
                if (!fetchResult.IsDefinitive)
                {
                    allProvidersDefinitive = false;
                }

                continue;
            }

            if (found is null || fetchResult.Lyrics.HasWordTiming)
            {
                found = fetchResult.Lyrics;
                _logger.LogInformation(
                    "Lyrics for track {TrackId} ({Artist} - {Title}) found via {Provider} (word timing: {HasWordTiming})",
                    track.Id,
                    track.Artist,
                    track.Title,
                    provider.ProviderName,
                    found.HasWordTiming);
            }

            if (found.HasWordTiming || found.IsInstrumental)
            {
                break;
            }
        }

        return (found, allProvidersDefinitive);
    }
}
