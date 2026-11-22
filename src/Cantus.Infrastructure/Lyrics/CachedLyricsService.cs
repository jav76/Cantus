using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

public sealed class CachedLyricsService : ILyricsProvider
{
    private readonly ILyricsCacheRepository _cacheRepository;
    private readonly IReadOnlyList<ILyricsFetchProvider> _providers;
    private readonly IReadOnlyList<ILyricsFetchProvider> _wordTimingProviders;
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
        _wordTimingProviders = providers
            .Where(p => p.SupportsWordTiming)
            .ToList();
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
            (bool queriedProviders, SyncedLyrics? upgraded) = await TryUpgradeToWordTimingAsync(
                track,
                cached,
                cancellationToken);
            if (!queriedProviders)
            {
                return cached;
            }

            return await PreferOverrideAppliedDuringFetchAsync(track, upgraded ?? cached, cancellationToken);
        }

        // 3. Query the provider chain in order, preferring a word-timed result
        _logger.LogInformation(
            "Cache miss for track {TrackId} ({Artist} - {Title}). Querying {ProviderCount} lyrics providers...",
            track.Id,
            track.Artist,
            track.Title,
            _providers.Count);

        LyricsFetchResult result = await RefreshFromProvidersAsync(track, cancellationToken);
        return await PreferOverrideAppliedDuringFetchAsync(track, result.Lyrics, cancellationToken);
    }

    /// <summary>
    /// Re-reads the override after a wait on the providers, so an override the owner applied in the
    /// meantime wins whatever the providers returned. The fetched result stays cached for when the
    /// override is cleared.
    /// </summary>
    private async Task<SyncedLyrics?> PreferOverrideAppliedDuringFetchAsync(
        TrackInfo track,
        SyncedLyrics? fetched,
        CancellationToken cancellationToken)
    {
        LyricsOverride? applied = await _cacheRepository.GetLyricsOverrideAsync(track.Id, cancellationToken);
        return applied is null ? fetched : applied.Lyrics;
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

        (SyncedLyrics? found, bool allProvidersDefinitive) = await QueryProvidersAsync(
            _providers,
            track,
            cancellationToken);
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
    /// Re-queries the providers that can return word timing for a cached entry that has none, at most
    /// <see cref="LyricsCacheOptions.WordTimingUpgradeMaxAttempts"/> times and no more often than
    /// <see cref="LyricsCacheOptions.WordTimingUpgradeIntervalDays"/>. The attempt is recorded before
    /// the fetch, so a failing provider cannot cause a refetch on every play. Reports whether the
    /// providers were queried, and returns the word-timed lyrics after saving them, or null to keep
    /// the cached entry as it is.
    /// </summary>
    private async Task<(bool QueriedProviders, SyncedLyrics? Upgraded)> TryUpgradeToWordTimingAsync(
        TrackInfo track,
        SyncedLyrics cached,
        CancellationToken cancellationToken)
    {
        if (cached.IsInstrumental
            || cached.HasWordTiming
            || _options.WordTimingUpgradeMaxAttempts <= 0
            || _wordTimingProviders.Count == 0)
        {
            return (false, null);
        }

        bool started;
        try
        {
            started = await _cacheRepository.TryStartWordTimingUpgradeAsync(
                track.Id,
                _options.WordTimingUpgradeMaxAttempts,
                TimeSpan.FromDays(_options.WordTimingUpgradeIntervalDays),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not record a word timing upgrade attempt for track {TrackId}; serving the cached lyrics.",
                track.Id);
            return (false, null);
        }

        if (!started)
        {
            return (false, null);
        }

        (SyncedLyrics? found, _) = await QueryProvidersAsync(_wordTimingProviders, track, cancellationToken);
        if (found is null || !found.HasWordTiming)
        {
            _logger.LogDebug(
                "No word-timed lyrics yet for cached track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return (true, null);
        }

        _logger.LogInformation(
            "Upgraded cached lyrics for track {TrackId} ({Artist} - {Title}) to word timing",
            track.Id,
            track.Artist,
            track.Title);
        await _cacheRepository.SaveLyricsAsync(found, cancellationToken: cancellationToken);
        return (true, found);
    }

    /// <summary>
    /// Walks the providers in order. The first result found is kept unless a later provider returns
    /// word timing; the walk stops at the first word-timed or instrumental result.
    /// </summary>
    private async Task<(SyncedLyrics? Found, bool AllProvidersDefinitive)> QueryProvidersAsync(
        IReadOnlyList<ILyricsFetchProvider> providers,
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        SyncedLyrics? found = null;
        bool allProvidersDefinitive = true;

        foreach (ILyricsFetchProvider provider in providers)
        {
            LyricsFetchResult? fetchResult = await TryFetchFromProviderAsync(provider, track, cancellationToken);
            if (fetchResult is null)
            {
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

    private async Task<LyricsFetchResult?> TryFetchFromProviderAsync(
        ILyricsFetchProvider provider,
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.FetchLyricsAsync(track, cancellationToken);
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
            return null;
        }
    }
}
