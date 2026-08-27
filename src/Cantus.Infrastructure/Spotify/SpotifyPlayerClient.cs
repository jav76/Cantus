using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace Cantus.Infrastructure.Spotify;

public sealed class SpotifyPlayerClient : ISpotifyPlayerClient
{
    private readonly ILogger<SpotifyPlayerClient> _logger;

    public SpotifyPlayerClient(ILogger<SpotifyPlayerClient> logger)
    {
        _logger = logger;
    }

    public async Task<PlaybackState?> GetCurrentPlaybackAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            var spotify = new SpotifyClient(accessToken);
            var tRequest = DateTimeOffset.UtcNow;
            var playback = await spotify.Player.GetCurrentPlayback(cancellationToken);
            var tResponse = DateTimeOffset.UtcNow;

            if (playback is null || playback.Item is null)
            {
                return null;
            }

            TrackInfo trackInfo;
            if (playback.Item is FullTrack fullTrack)
            {
                trackInfo = new TrackInfo
                {
                    Id = fullTrack.Id ?? string.Empty,
                    Title = fullTrack.Name,
                    Artist = string.Join(", ", fullTrack.Artists.Select(a => a.Name)),
                    Album = fullTrack.Album?.Name,
                    AlbumArtUrl = fullTrack.Album?.Images?.FirstOrDefault()?.Url,
                    Duration = TimeSpan.FromMilliseconds(fullTrack.DurationMs),
                    IsExplicit = fullTrack.Explicit
                };
            }
            else if (playback.Item is FullEpisode fullEpisode)
            {
                trackInfo = new TrackInfo
                {
                    Id = fullEpisode.Id ?? string.Empty,
                    Title = fullEpisode.Name,
                    Artist = fullEpisode.Show?.Name ?? "Podcast",
                    Album = fullEpisode.Show?.Name,
                    AlbumArtUrl = fullEpisode.Images?.FirstOrDefault()?.Url,
                    Duration = TimeSpan.FromMilliseconds(fullEpisode.DurationMs),
                    IsExplicit = fullEpisode.Explicit
                };
            }
            else
            {
                return null;
            }

            // Anchor snapshot to server clock at request midpoint to ensure exact alignment with NTP SignalR sync
            var serverSnapshotTimestamp = tRequest + TimeSpan.FromMilliseconds((tResponse - tRequest).TotalMilliseconds / 2.0);

            return new PlaybackState
            {
                CurrentTrack = trackInfo,
                Progress = TimeSpan.FromMilliseconds(playback.ProgressMs),
                IsPlaying = playback.IsPlaying,
                TimestampUtc = serverSnapshotTimestamp,
                DeviceName = playback.Device?.Name,
                VolumePercent = playback.Device?.VolumePercent
            };
        }
        catch (APIUnauthorizedException)
        {
            _logger.LogWarning("Spotify token expired or unauthorized during playback poll.");
            throw;
        }
        catch (APITooManyRequestsException ex)
        {
            _logger.LogWarning("Spotify rate limit hit. Retry after {RetryAfter}s.", ex.RetryAfter);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error fetching current Spotify playback.");
            return null;
        }
    }
}
