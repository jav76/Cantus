using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Applies the owner's lyrics corrections for a track. Overrides are stored
/// apart from the cache, so cache expiry and negative caching never undo them;
/// re-fetching and clearing hand the track back to automatic matching through
/// <see cref="CachedLyricsService"/>.
/// </summary>
public sealed class LyricsOverrideService
{
    private readonly ILyricsCacheRepository _repository;
    private readonly IReadOnlyList<ILyricsFetchProvider> _providers;
    private readonly CachedLyricsService _automaticLyrics;
    private readonly ILogger<LyricsOverrideService> _logger;

    public LyricsOverrideService(
        ILyricsCacheRepository repository,
        IReadOnlyList<ILyricsFetchProvider> providers,
        CachedLyricsService automaticLyrics,
        ILogger<LyricsOverrideService> logger)
    {
        _repository = repository;
        _providers = providers;
        _automaticLyrics = automaticLyrics;
        _logger = logger;
    }

    public async Task<LyricsOverrideOutcome> UseCandidateAsync(
        TrackInfo track,
        string provider,
        string providerCandidateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        ILyricsFetchProvider? source = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, provider, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return LyricsOverrideOutcome.Rejected(LyricsOverrideStatus.UnknownProvider);
        }

        SyncedLyrics? lyrics;
        try
        {
            lyrics = await source.GetCandidateLyricsAsync(track, providerCandidateId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Includes HttpClient timeouts, which surface as a cancellation
            // the caller did not request.
            _logger.LogWarning(
                ex,
                "Lyrics provider {Provider} could not load candidate {CandidateId} for track {TrackId}",
                source.ProviderName,
                providerCandidateId,
                track.Id);
            return LyricsOverrideOutcome.Rejected(LyricsOverrideStatus.ProviderUnavailable);
        }

        if (lyrics is null)
        {
            return LyricsOverrideOutcome.Rejected(LyricsOverrideStatus.CandidateNotFound);
        }

        await _repository.SaveLyricsOverrideAsync(
            LyricsOverride.ForCandidate(source.ProviderName, providerCandidateId, lyrics),
            cancellationToken);

        _logger.LogInformation(
            "Lyrics for track {TrackId} ({Artist} - {Title}) overridden with {Provider} candidate {CandidateId}",
            track.Id,
            track.Artist,
            track.Title,
            source.ProviderName,
            providerCandidateId);

        return LyricsOverrideOutcome.Applied(lyrics);
    }

    public async Task MarkNoLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        await _repository.SaveLyricsOverrideAsync(LyricsOverride.ForNoLyrics(track.Id), cancellationToken);

        _logger.LogInformation(
            "Track {TrackId} ({Artist} - {Title}) marked as having no lyrics",
            track.Id,
            track.Artist,
            track.Title);
    }

    /// <summary>
    /// Matches the track again from the providers, skipping the caches. Only a
    /// definitive answer replaces the cached match (including a "not found"
    /// mark) and drops the override; when a provider cannot be reached nothing
    /// is changed and the outcome is ProviderUnavailable.
    /// </summary>
    public async Task<LyricsOverrideOutcome> RefetchAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        _logger.LogInformation(
            "Re-fetching lyrics for track {TrackId} ({Artist} - {Title})",
            track.Id,
            track.Artist,
            track.Title);

        LyricsFetchResult result = await _automaticLyrics.RefreshFromProvidersAsync(track, cancellationToken);
        if (!result.IsDefinitive)
        {
            return LyricsOverrideOutcome.Rejected(LyricsOverrideStatus.ProviderUnavailable);
        }

        await _repository.RemoveLyricsOverrideAsync(track.Id, cancellationToken);
        return LyricsOverrideOutcome.Applied(result.Lyrics ?? _automaticLyrics.InferInstrumentalFromTitle(track));
    }

    /// <summary>
    /// Drops the override and returns the automatic match, served from the
    /// cache when it still holds one.
    /// </summary>
    public async Task<SyncedLyrics?> ClearAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        await _repository.RemoveLyricsOverrideAsync(track.Id, cancellationToken);

        _logger.LogInformation(
            "Lyrics override cleared for track {TrackId} ({Artist} - {Title})",
            track.Id,
            track.Artist,
            track.Title);

        return await _automaticLyrics.GetLyricsAsync(track, cancellationToken);
    }
}
