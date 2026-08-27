using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

public sealed class CachedLyricsService : ILyricsProvider
{
    private readonly ILyricsCacheRepository _cacheRepository;
    private readonly LrclibLyricsProvider _lrclibProvider;
    private readonly LrclibOptions _options;
    private readonly ILogger<CachedLyricsService> _logger;

    public CachedLyricsService(
        ILyricsCacheRepository cacheRepository,
        LrclibLyricsProvider lrclibProvider,
        IOptions<LrclibOptions> options,
        ILogger<CachedLyricsService> logger)
    {
        _cacheRepository = cacheRepository;
        _lrclibProvider = lrclibProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncedLyrics?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

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

        // 3. Query LRCLIB
        _logger.LogInformation(
            "Cache miss for track {TrackId} ({Artist} - {Title}). Fetching from LRCLIB...",
            track.Id,
            track.Artist,
            track.Title);

        SyncedLyrics? freshLyrics = await _lrclibProvider.GetLyricsAsync(track, cancellationToken);

        if (freshLyrics is not null)
        {
            await _cacheRepository.SaveLyricsAsync(freshLyrics, cancellationToken: cancellationToken);
            return freshLyrics;
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
