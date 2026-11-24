using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Spotify;
using Microsoft.Extensions.Logging;

namespace Cantus.Server.Services;

/// <summary>
/// Plays or queues a song lookup guess. A library guess carries its Spotify
/// track id; a provider guess is resolved on Spotify at click time, so a lookup
/// itself spends no Spotify quota. The track URI is always built here from a
/// validated id. Lives in the server because a play schedules the same
/// follow-up polls as the hub's transport commands.
/// </summary>
public sealed class SongPlaybackService : ISongPlaybackService
{
    private const string TRACK_URI_PREFIX = "spotify:track:";

    private readonly ISpotifyPlayerClient _playerClient;
    private readonly SpotifyTrackResolver _trackResolver;
    private readonly SpotifyRateLimitGate _rateLimitGate;
    private readonly IPlaybackSessionRegistry _registry;
    private readonly ILogger<SongPlaybackService> _logger;

    public SongPlaybackService(
        ISpotifyPlayerClient playerClient,
        SpotifyTrackResolver trackResolver,
        SpotifyRateLimitGate rateLimitGate,
        IPlaybackSessionRegistry registry,
        ILogger<SongPlaybackService> logger)
    {
        _playerClient = playerClient;
        _trackResolver = trackResolver;
        _rateLimitGate = rateLimitGate;
        _registry = registry;
        _logger = logger;
    }

    public static bool IsValidTrackId(string? trackId)
    {
        return TrackIdentity.TryParseSpotifyTrackId(trackId, out _);
    }

    public static bool HasValidNameLengths(string? title, string? artist)
    {
        return (title?.Length ?? 0) <= LyricMatchConstants.MAX_NAME_CHARACTERS
            && (artist?.Length ?? 0) <= LyricMatchConstants.MAX_NAME_CHARACTERS;
    }

    public async Task<SongPlayResult> PlayAsync(
        string ownerUserId,
        string accessToken,
        SongPlayRequest request,
        CancellationToken cancellationToken = default)
    {
        bool byId = !string.IsNullOrEmpty(request.SpotifyTrackId);
        if (byId && !IsValidTrackId(request.SpotifyTrackId))
        {
            throw new ArgumentException("The Spotify track id is malformed.", nameof(request));
        }

        if (!byId && (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist)))
        {
            throw new ArgumentException("A track id, or a title and an artist, is required.", nameof(request));
        }

        if (!byId && !HasValidNameLengths(request.Title, request.Artist))
        {
            throw new ArgumentException("The title or artist is too long.", nameof(request));
        }

        if (_rateLimitGate.IsLimited)
        {
            return RateLimited();
        }

        string trackId;
        TrackInfo? resolved = null;
        if (byId)
        {
            trackId = request.SpotifyTrackId!;
        }
        else
        {
            try
            {
                resolved = await _trackResolver.ResolveAsync(
                    accessToken,
                    request.Title!,
                    request.Artist!,
                    request.DurationMs,
                    cancellationToken);
            }
            catch (SpotifyRateLimitedException ex)
            {
                return new SongPlayResult(SongPlayStatus.RateLimited, RetryAfter: ex.RetryAfter);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Spotify track search failed for a lookup guess");
                return new SongPlayResult(SongPlayStatus.Failed);
            }

            if (resolved is null)
            {
                return new SongPlayResult(SongPlayStatus.NotOnSpotify);
            }

            if (!IsValidTrackId(resolved.Id))
            {
                _logger.LogWarning("Spotify search returned a track with an unexpected id format");
                return new SongPlayResult(SongPlayStatus.Failed, resolved);
            }

            trackId = resolved.Id;
        }

        string trackUri = TRACK_URI_PREFIX + trackId;
        PlayerCommandResult commandResult = request.Action == SongPlayAction.Queue
            ? await _playerClient.AddToQueueAsync(accessToken, trackUri, cancellationToken)
            : await _playerClient.PlayTrackAsync(accessToken, trackUri, cancellationToken);

        _logger.LogInformation(
            "Lookup {Action} of track {TrackId} for Session {SessionRef}: {Result}",
            request.Action,
            trackId,
            SessionLogRef.For(ownerUserId),
            commandResult);

        if (commandResult == PlayerCommandResult.Success && request.Action == SongPlayAction.Play)
        {
            _registry.RequestUserActivity(ownerUserId);
            PlaybackFollowUpPolls.Schedule(_registry, ownerUserId, _logger);
        }

        return commandResult switch
        {
            PlayerCommandResult.Success => new SongPlayResult(SongPlayStatus.Success, resolved),
            PlayerCommandResult.NoActiveDevice => new SongPlayResult(SongPlayStatus.NoActiveDevice, resolved),
            PlayerCommandResult.MissingPermissions => new SongPlayResult(SongPlayStatus.MissingPermissions, resolved),
            _ when _rateLimitGate.IsLimited => RateLimited(resolved),
            _ => new SongPlayResult(SongPlayStatus.Failed, resolved)
        };
    }

    private SongPlayResult RateLimited(TrackInfo? track = null)
    {
        return new SongPlayResult(SongPlayStatus.RateLimited, track, _rateLimitGate.Remaining);
    }
}
