using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.CoverArt;

/// <summary>
/// The open catalogue: MusicBrainz names the release groups a track belongs to, and the Cover
/// Art Archive says which of them has a front cover.
/// </summary>
public sealed class MusicBrainzCoverArtCatalog : ICoverArtCatalog
{
    public const int MAX_RELEASE_GROUPS_PROBED = 3;

    private readonly MusicBrainzClient _musicBrainz;
    private readonly CoverArtArchiveClient _coverArtArchive;
    private readonly ILogger<MusicBrainzCoverArtCatalog> _logger;

    public MusicBrainzCoverArtCatalog(
        MusicBrainzClient musicBrainz,
        CoverArtArchiveClient coverArtArchive,
        ILogger<MusicBrainzCoverArtCatalog> logger)
    {
        _musicBrainz = musicBrainz;
        _coverArtArchive = coverArtArchive;
        _logger = logger;
    }

    public string CatalogName => MusicBrainzClient.PROVIDER_NAME;

    public async Task<CoverArtFetchResult> FetchCoverArtAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        try
        {
            IReadOnlyList<MusicBrainzRecording> recordings = await _musicBrainz.SearchRecordingsAsync(
                track,
                cancellationToken);
            IReadOnlyList<Guid> releaseGroups = MusicBrainzReleaseMatcher.SelectReleaseGroups(
                recordings,
                track,
                MAX_RELEASE_GROUPS_PROBED);

            foreach (Guid releaseGroupId in releaseGroups)
            {
                string? artUrl = await _coverArtArchive.FindFrontCoverAsync(releaseGroupId, cancellationToken);
                if (artUrl is not null)
                {
                    return CoverArtFetchResult.Found(artUrl);
                }
            }

            return CoverArtFetchResult.NotFound();
        }
        catch (ProviderRateLimitedException ex)
        {
            _logger.LogDebug(
                "Skipped cover art for track {TrackId}: {Provider} requests are paused for {RetryAfterSeconds} s",
                track.Id,
                ex.ProviderName,
                ex.RetryAfter.TotalSeconds);
            return CoverArtFetchResult.Unavailable();
        }
        catch (Exception ex) when (CachedCoverArtService.IsNotCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "Failed to look up cover art for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return CoverArtFetchResult.Unavailable();
        }
    }
}
