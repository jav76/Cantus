using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace Cantus.Infrastructure.Spotify;

public sealed class SpotifyPlayerClient : ISpotifyPlayerClient
{
    private readonly ILogger<SpotifyPlayerClient> _logger;
    private readonly SpotifyRateLimitGate _rateLimitGate;
    private readonly Func<string, SpotifyClient> _clientFactory;

    public SpotifyPlayerClient(
        ILogger<SpotifyPlayerClient> logger,
        SpotifyRateLimitGate? rateLimitGate = null)
        : this(logger, accessToken => new SpotifyClient(accessToken), rateLimitGate)
    {
    }

    /// <summary>Test seam: lets tests route SDK calls through a fake HTTP handler.</summary>
    internal SpotifyPlayerClient(
        ILogger<SpotifyPlayerClient> logger,
        Func<string, SpotifyClient> clientFactory,
        SpotifyRateLimitGate? rateLimitGate = null)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _rateLimitGate = rateLimitGate ?? new(TimeProvider.System);
    }

    public async Task<PlaybackState?> GetCurrentPlaybackAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            SpotifyClient spotify = _clientFactory(accessToken);
            DateTimeOffset tRequest = DateTimeOffset.UtcNow;
            CurrentlyPlayingContext? playback = await spotify.Player.GetCurrentPlayback(cancellationToken);
            DateTimeOffset tResponse = DateTimeOffset.UtcNow;

            if (playback is null || playback.Item is null)
            {
                return null;
            }

            TrackInfo trackInfo;
            if (playback.Item is FullTrack fullTrack)
            {
                trackInfo = SpotifyTrackMapper.ToTrackInfo(fullTrack);
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
            TimeSpan roundTripDuration = TimeSpan.FromMilliseconds((tResponse - tRequest).TotalMilliseconds / 2.0);
            DateTimeOffset serverSnapshotTimestamp = tRequest + roundTripDuration;

            return new PlaybackState
            {
                CurrentTrack = trackInfo,
                Progress = TimeSpan.FromMilliseconds(playback.ProgressMs),
                IsPlaying = playback.IsPlaying,
                TimestampUtc = serverSnapshotTimestamp,
                DeviceName = playback.Device?.Name,
                VolumePercent = playback.Device?.VolumePercent,
                IsShuffled = playback.ShuffleState,
                RepeatMode = playback.RepeatState ?? "off"
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

    public Task<PlayerCommandResult> PausePlaybackAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePlayerCommandAsync(
            accessToken,
            "pause",
            spotify => spotify.Player.PausePlayback(new PlayerPausePlaybackRequest(), cancellationToken));
    }

    public Task<PlayerCommandResult> ResumePlaybackAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePlayerCommandAsync(
            accessToken,
            "resume",
            spotify => spotify.Player.ResumePlayback(new PlayerResumePlaybackRequest(), cancellationToken));
    }

    public Task<PlayerCommandResult> SkipToNextAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePlayerCommandAsync(
            accessToken,
            "next",
            spotify => spotify.Player.SkipNext(new PlayerSkipNextRequest(), cancellationToken));
    }

    public Task<PlayerCommandResult> SkipToPreviousAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        return ExecutePlayerCommandAsync(
            accessToken,
            "previous",
            spotify => spotify.Player.SkipPrevious(new PlayerSkipPreviousRequest(), cancellationToken));
    }

    public Task<PlayerCommandResult> SetVolumeAsync(string accessToken, int volumePercent, CancellationToken cancellationToken = default)
    {
        int clamped = Math.Clamp(volumePercent, 0, 100);
        return ExecutePlayerCommandAsync(
            accessToken,
            "volume",
            spotify => spotify.Player.SetVolume(new PlayerVolumeRequest(clamped), cancellationToken));
    }

    public Task<PlayerCommandResult> SeekPlaybackAsync(string accessToken, long positionMs, CancellationToken cancellationToken = default)
    {
        long clamped = Math.Max(0, positionMs);
        return ExecutePlayerCommandAsync(
            accessToken,
            "seek",
            spotify => spotify.Player.SeekTo(new PlayerSeekToRequest(clamped), cancellationToken));
    }

    public Task<PlayerCommandResult> SetShuffleAsync(string accessToken, bool enabled, CancellationToken cancellationToken = default)
    {
        return ExecutePlayerCommandAsync(
            accessToken,
            "shuffle",
            spotify => spotify.Player.SetShuffle(new PlayerShuffleRequest(enabled), cancellationToken));
    }

    public Task<PlayerCommandResult> SetRepeatAsync(string accessToken, string repeatMode, CancellationToken cancellationToken = default)
    {
        PlayerSetRepeatRequest.State? state = repeatMode switch
        {
            "off" => PlayerSetRepeatRequest.State.Off,
            "track" => PlayerSetRepeatRequest.State.Track,
            "context" => PlayerSetRepeatRequest.State.Context,
            _ => null
        };

        if (state is null)
        {
            _logger.LogWarning("Ignoring unknown repeat mode {RepeatMode}", repeatMode);
            return Task.FromResult(PlayerCommandResult.Failed);
        }

        return ExecutePlayerCommandAsync(
            accessToken,
            "repeat",
            spotify => spotify.Player.SetRepeat(new PlayerSetRepeatRequest(state.Value), cancellationToken));
    }

    public Task<PlayerCommandResult> PlayTrackAsync(
        string accessToken,
        string trackUri,
        CancellationToken cancellationToken = default)
    {
        return ExecutePlayerCommandAsync(
            accessToken,
            "play-track",
            spotify => spotify.Player.ResumePlayback(
                new PlayerResumePlaybackRequest { Uris = new List<string> { trackUri } },
                cancellationToken));
    }

    public Task<PlayerCommandResult> AddToQueueAsync(
        string accessToken,
        string trackUri,
        CancellationToken cancellationToken = default)
    {
        return ExecutePlayerCommandAsync(
            accessToken,
            "queue",
            spotify => spotify.Player.AddToQueue(new PlayerAddToQueueRequest(trackUri), cancellationToken));
    }

    private async Task<PlayerCommandResult> ExecutePlayerCommandAsync(
        string accessToken,
        string commandName,
        Func<SpotifyClient, Task<bool>> command)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return PlayerCommandResult.Failed;
        }

        try
        {
            SpotifyClient spotify = _clientFactory(accessToken);
            bool accepted = await command(spotify);
            return accepted ? PlayerCommandResult.Success : PlayerCommandResult.Failed;
        }
        catch (APITooManyRequestsException ex)
        {
            ReportRateLimit(commandName, ex.RetryAfter);
            return PlayerCommandResult.Failed;
        }
        catch (APIException ex)
        {
            // 403 means the session lacks the user-modify-playback-state scope
            // (re-link required) or the account is not Premium; 404 means no
            // active playback device.
            _logger.LogWarning(
                "Spotify player command {Command} failed with {StatusCode}: {Message}",
                commandName,
                ex.Response?.StatusCode,
                ex.Message);

            return ex.Response?.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => PlayerCommandResult.MissingPermissions,
                System.Net.HttpStatusCode.NotFound => PlayerCommandResult.NoActiveDevice,
                _ => PlayerCommandResult.Failed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error sending Spotify player command {Command}.", commandName);
            return PlayerCommandResult.Failed;
        }
    }

    private void ReportRateLimit(string commandName, TimeSpan retryAfter)
    {
        TimeSpan applied = _rateLimitGate.Report(retryAfter);
        _logger.LogWarning(
            "Spotify rate limit hit on player command {Command}. Pausing Spotify calls until {RateLimitUntil} UTC (Retry after {RetryAfter})",
            commandName,
            _rateLimitGate.LimitedUntilUtc,
            applied);
    }
}
