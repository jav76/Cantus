using Cantus.Core.Models;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Gathers lyrics candidates for a track from every enabled provider. Uses the
/// same ordered provider list as <see cref="CachedLyricsService"/>, so a
/// provider disabled in configuration is never queried.
/// </summary>
public sealed class LyricsCandidateService
{
    private readonly IReadOnlyList<ILyricsFetchProvider> _providers;
    private readonly ILogger<LyricsCandidateService> _logger;

    public LyricsCandidateService(
        IReadOnlyList<ILyricsFetchProvider> providers,
        ILogger<LyricsCandidateService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<LyricsCandidateList> GetCandidatesAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        IReadOnlyList<LyricsCandidate>?[] perProvider = await Task.WhenAll(
            _providers.Select(p => QueryProviderAsync(p, track, cancellationToken)));

        List<LyricsCandidate> candidates = new();
        List<string> unavailable = new();
        for (int i = 0; i < _providers.Count; i++)
        {
            if (perProvider[i] is { } providerCandidates)
            {
                candidates.AddRange(providerCandidates);
            }
            else
            {
                unavailable.Add(_providers[i].ProviderName);
            }
        }

        return new LyricsCandidateList(candidates, unavailable);
    }

    /// <returns>The provider's candidates, or null when the provider could not answer.</returns>
    private async Task<IReadOnlyList<LyricsCandidate>?> QueryProviderAsync(
        ILyricsFetchProvider provider,
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetCandidatesAsync(track, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Includes HttpClient timeouts, which surface as a cancellation
            // the caller did not request.
            _logger.LogWarning(
                ex,
                "Lyrics provider {Provider} could not list candidates for track {TrackId} ({Artist} - {Title})",
                provider.ProviderName,
                track.Id,
                track.Artist,
                track.Title);
            return null;
        }
    }
}
