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
    private readonly ISessionTokenResolver _sessionResolver;
    private readonly ILogger<PlaybackHub> _logger;

    public PlaybackHub(
        IPlaybackSessionRegistry registry,
        ILyricsCacheRepository lyricsCache,
        ISpotifyAuthService authService,
        ISessionTokenResolver sessionResolver,
        ILogger<PlaybackHub> logger)
    {
        _registry = registry;
        _lyricsCache = lyricsCache;
        _authService = authService;
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
                userSession = await _authService.GetSessionAsync(resolvedSessionId);
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

        UserSession? userSession = await _authService.GetSessionAsync(callingSessionId);
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
}
