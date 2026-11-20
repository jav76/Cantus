using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Cantus.Server.Hubs;

public sealed class PlaybackHub : Hub<IPlaybackClient>
{
    private readonly IPlaybackSessionRegistry _registry;
    private readonly ILyricsCacheRepository _lyricsCache;
    private readonly ISpotifyAuthService _authService;
    private readonly ISpotifyPlayerClient _playerClient;
    private readonly ISessionTokenResolver _sessionResolver;
    private readonly ILogger<PlaybackHub> _logger;

    public PlaybackHub(
        IPlaybackSessionRegistry registry,
        ILyricsCacheRepository lyricsCache,
        ISpotifyAuthService authService,
        ISpotifyPlayerClient playerClient,
        ISessionTokenResolver sessionResolver,
        ILogger<PlaybackHub> logger)
    {
        _registry = registry;
        _lyricsCache = lyricsCache;
        _authService = authService;
        _playerClient = playerClient;
        _sessionResolver = sessionResolver;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        string? resolvedSessionId = null;
        UserSession? userSession = null;

        try
        {
            HttpContext? httpContext = Context.GetHttpContext();
            resolvedSessionId = _sessionResolver.ResolveSessionId(httpContext);

            if (!string.IsNullOrWhiteSpace(resolvedSessionId))
            {
                userSession = await _authService.GetSessionByIdAsync(resolvedSessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve session for connection {ConnectionId}", Context.ConnectionId);
        }

        string? userId = userSession?.Id;
        _registry.RegisterConnection(Context.ConnectionId, userId);

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        _logger.LogInformation(
            "Client connected to PlaybackHub: {ConnectionId} (User: {UserId}, Total: {Count})",
            Context.ConnectionId,
            userId ?? "Unauthenticated",
            _registry.ConnectedClientsCount);

        try
        {
            if (userSession is not null && !string.IsNullOrEmpty(userId))
            {
                UserPlaybackSnapshot? snapshot = _registry.GetUserState(userId);
                bool isPlaying = snapshot?.PlaybackState?.IsPlaying ?? false;
                List<AuthorizedSessionDto> sessionDtos = new() { userSession.ToDto(isPlaying) };

                if (snapshot?.PlaybackState is not null)
                {
                    await Clients.Caller.ReceivePlaybackState(
                        snapshot.PlaybackState.ToDto(snapshot.UserId, snapshot.DisplayName));
                }

                if (snapshot?.Lyrics is not null)
                {
                    await Clients.Caller.ReceiveLyrics(snapshot.Lyrics.ToDto());
                }

                if (snapshot?.PlaybackState?.CurrentTrack is not null)
                {
                    await Clients.Caller.ReceiveTrackOffset(new TrackOffsetDto
                    {
                        TrackId = snapshot.PlaybackState.CurrentTrack.Id,
                        OffsetMs = snapshot.TrackOffsetMs
                    });
                }

                await Clients.Caller.ReceiveSessions(sessionDtos);

                await Clients.Caller.ReceiveDiagnostics(new DiagnosticsDto
                {
                    ConnectedClients = _registry.ConnectedClientsCount,
                    AuthorizedSessions = sessionDtos.Count,
                    PollerStatus = isPlaying ? "Active (Playing)" : "Idle",
                    ActiveUserId = userSession.Id,
                    ActiveUserName = userSession.DisplayName,
                    ServerTimeUtc = DateTimeOffset.UtcNow
                });
            }
            else
            {
                // Unauthenticated client
                await Clients.Caller.ReceiveSessions(new List<AuthorizedSessionDto>());
                await Clients.Caller.ReceiveDiagnostics(new DiagnosticsDto
                {
                    ConnectedClients = _registry.ConnectedClientsCount,
                    AuthorizedSessions = 0,
                    PollerStatus = "Idle",
                    ActiveUserId = null,
                    ActiveUserName = null,
                    ServerTimeUtc = DateTimeOffset.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error sending initial state to newly connected client {ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        _registry.UnregisterConnection(Context.ConnectionId);

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        _logger.LogInformation(
            "Client disconnected from PlaybackHub: {ConnectionId} (Total: {Count})",
            Context.ConnectionId,
            _registry.ConnectedClientsCount);

        await base.OnDisconnectedAsync(exception);
    }

    public Task<ClockSyncResponse> SyncClock(long clientSendTimeMs)
    {
        long serverReceiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long serverSendTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return Task.FromResult(new ClockSyncResponse(
            clientSendTimeMs,
            serverReceiveTime,
            serverSendTime));
    }

    public async Task RegisterClientLogin(string clientId)
    {
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"client_{clientId}");
        }
    }

    public async Task SubscribeToUser(string? userId)
    {
        string? callingSessionId = _sessionResolver.ResolveSessionId(Context.GetHttpContext());
        if (string.IsNullOrEmpty(callingSessionId))
        {
            return;
        }

        UserSession? userSession = await _authService.GetSessionByIdAsync(callingSessionId);
        if (userSession is null || (userId is not null && userSession.Id != userId))
        {
            return;
        }

        string? prevUserId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (!string.IsNullOrEmpty(prevUserId) && prevUserId != userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{prevUserId}");
        }

        _registry.SetConnectionSubscription(Context.ConnectionId, userId);

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");

            UserPlaybackSnapshot? snapshot = _registry.GetUserState(userId);
            if (snapshot is not null)
            {
                if (snapshot.PlaybackState is not null)
                {
                    await Clients.Caller.ReceivePlaybackState(
                        snapshot.PlaybackState.ToDto(snapshot.UserId, snapshot.DisplayName));
                }

                if (snapshot.Lyrics is not null)
                {
                    await Clients.Caller.ReceiveLyrics(snapshot.Lyrics.ToDto());
                }

                if (snapshot.PlaybackState?.CurrentTrack is not null)
                {
                    await Clients.Caller.ReceiveTrackOffset(new TrackOffsetDto
                    {
                        TrackId = snapshot.PlaybackState.CurrentTrack.Id,
                        OffsetMs = snapshot.TrackOffsetMs
                    });
                }
            }

            bool isPlaying = snapshot?.PlaybackState?.IsPlaying ?? false;
            await Clients.Caller.ReceiveDiagnostics(new DiagnosticsDto
            {
                ConnectedClients = _registry.ConnectedClientsCount,
                AuthorizedSessions = 1,
                PollerStatus = isPlaying ? "Active (Playing)" : "Idle",
                ActiveUserId = userId,
                ActiveUserName = userSession.DisplayName,
                ServerTimeUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public async Task SetTrackOffset(string trackId, int offsetMs)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return;
        }

        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        _logger.LogInformation("Setting track offset for {TrackId}: {OffsetMs}ms", trackId, offsetMs);

        await _lyricsCache.SetTrackOffsetAsync(trackId, offsetMs);

        UserPlaybackSnapshot? userSnapshot = _registry.GetUserState(userId);
        if (userSnapshot?.PlaybackState?.CurrentTrack?.Id == trackId)
        {
            _registry.UpdateUserState(
                userSnapshot.UserId,
                userSnapshot.DisplayName,
                userSnapshot.PlaybackState,
                userSnapshot.Lyrics,
                offsetMs);
        }

        await Clients.Group($"user_{userId}").ReceiveTrackOffset(new TrackOffsetDto
        {
            TrackId = trackId,
            OffsetMs = offsetMs
        });
    }

    public Task ReportClientVisibility(bool isVisible)
    {
        _registry.SetConnectionVisibility(Context.ConnectionId, isVisible);
        if (isVisible)
        {
            string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
            if (!string.IsNullOrEmpty(userId))
            {
                _registry.RequestUserActivity(userId);
            }
        }

        return Task.CompletedTask;
    }

    public Task RefreshPlayback()
    {
        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (!string.IsNullOrEmpty(userId))
        {
            _registry.RequestUserActivity(userId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes a playback transport command ("pause", "resume", "next",
    /// "previous") against the caller's own Spotify session. The result tells
    /// the client why a command was rejected (missing
    /// user-modify-playback-state scope on sessions linked before the scope was
    /// added, non-Premium account, or no active device) so it can surface a
    /// hint instead of failing silently. On success polls are requested so
    /// every connected client sees the new state quickly. The result crosses
    /// the wire as an int: the trimmed WASM client cannot deserialize enum
    /// return values, so both ends cast PlayerCommandResult explicitly.
    /// </summary>
    public Task<int> SendPlayerCommand(string command)
    {
        return ExecutePlayerActionAsync(command, (token) => command switch
        {
            "pause" => _playerClient.PausePlaybackAsync(token),
            "resume" => _playerClient.ResumePlaybackAsync(token),
            "next" => _playerClient.SkipToNextAsync(token),
            "previous" => _playerClient.SkipToPreviousAsync(token),
            _ => Task.FromResult(PlayerCommandResult.Failed)
        });
    }

    public Task<int> SetPlayerVolume(int volumePercent)
    {
        return ExecutePlayerActionAsync(
            "volume",
            token => _playerClient.SetVolumeAsync(token, volumePercent));
    }

    public Task<int> SeekPlayback(long positionMs)
    {
        return ExecutePlayerActionAsync(
            "seek",
            token => _playerClient.SeekPlaybackAsync(token, positionMs));
    }

    public Task<int> SetShuffle(bool enabled)
    {
        return ExecutePlayerActionAsync(
            "shuffle",
            token => _playerClient.SetShuffleAsync(token, enabled));
    }

    public Task<int> SetRepeat(string repeatMode)
    {
        return ExecutePlayerActionAsync(
            "repeat",
            token => _playerClient.SetRepeatAsync(token, repeatMode));
    }

    private async Task<int> ExecutePlayerActionAsync(
        string commandName,
        Func<string, Task<PlayerCommandResult>> action)
    {
        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (string.IsNullOrEmpty(userId))
        {
            return (int)PlayerCommandResult.Failed;
        }

        UserSession? session = await _authService.GetSessionForUserAsync(userId);
        if (session is null || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return (int)PlayerCommandResult.Failed;
        }

        _logger.LogInformation("Player command {Command} requested by user {UserId}", commandName, userId);

        PlayerCommandResult result = await action(session.AccessToken);

        if (result == PlayerCommandResult.Success)
        {
            _registry.RequestUserActivity(userId);
            ScheduleFollowUpPolls(userId);
        }

        return (int)result;
    }

    // Spotify's API is eventually consistent: a poll fired immediately after
    // an accepted command often still returns the previous state. The first
    // follow-up gives Spotify time to commit the change; the second is a
    // safety net for slower propagation.
    private const int FIRST_FOLLOW_UP_POLL_DELAY_MS = 400;
    private const int SECOND_FOLLOW_UP_POLL_DELAY_MS = 1100;

    /// <summary>
    /// Two short follow-up polls after an accepted player command, so every
    /// connected client sees the committed state quickly instead of waiting
    /// for the next adaptive poll seconds later.
    /// </summary>
    private void ScheduleFollowUpPolls(string userId)
    {
        IPlaybackSessionRegistry registry = _registry;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(FIRST_FOLLOW_UP_POLL_DELAY_MS));
                registry.RequestUserActivity(userId);
                await Task.Delay(TimeSpan.FromMilliseconds(SECOND_FOLLOW_UP_POLL_DELAY_MS));
                registry.RequestUserActivity(userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Follow-up playback poll failed for user {UserId}", userId);
            }
        });
    }
}
