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
    private readonly ILogger<PlaybackHub> _logger;

    public PlaybackHub(
        IPlaybackSessionRegistry registry,
        ILyricsCacheRepository lyricsCache,
        ISpotifyAuthService authService,
        ILogger<PlaybackHub> logger)
    {
        _registry = registry;
        _lyricsCache = lyricsCache;
        _authService = authService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        string? resolvedSessionId = null;
        UserSession? userSession = null;

        try
        {
            HttpContext? httpContext = Context.GetHttpContext();
            resolvedSessionId = ResolveSessionId(httpContext);

            if (!string.IsNullOrWhiteSpace(resolvedSessionId))
            {
                userSession = await _authService.GetSessionAsync(resolvedSessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve session for connection {ConnectionId}", Context.ConnectionId);
        }

        IReadOnlyList<UserSession> allSessions = await _authService.GetAllSessionsAsync() ?? Array.Empty<UserSession>();
        if (userSession is null && allSessions.Count > 0)
        {
            UserPlaybackSnapshot? activeSnapshot = _registry.GetActivePlaybackSnapshot();
            userSession = allSessions.FirstOrDefault(s => s.Id == activeSnapshot?.UserId) ?? allSessions[0];
        }
        else if (userSession is not null && allSessions.Count == 0)
        {
            allSessions = new List<UserSession> { userSession };
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
            List<AuthorizedSessionDto> sessionDtos = allSessions.Select(s =>
            {
                UserPlaybackSnapshot? userSnap = _registry.GetUserState(s.Id);
                bool isPlaying = userSnap?.PlaybackState?.IsPlaying ?? false;
                return s.ToDto(isPlaying);
            }).ToList();

            if (userSession is not null && !string.IsNullOrEmpty(userId))
            {
                UserPlaybackSnapshot? snapshot = _registry.GetUserState(userId);

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

                bool isPlaying = snapshot?.PlaybackState?.IsPlaying ?? false;
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

    public async Task SubscribeToUser(string? userId)
    {
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

            UserSession? userSession = await _authService.GetSessionAsync(userId);
            bool isPlaying = snapshot?.PlaybackState?.IsPlaying ?? false;
            await Clients.Caller.ReceiveDiagnostics(new DiagnosticsDto
            {
                ConnectedClients = _registry.ConnectedClientsCount,
                AuthorizedSessions = 1,
                PollerStatus = isPlaying ? "Active (Playing)" : "Idle",
                ActiveUserId = userId,
                ActiveUserName = userSession?.DisplayName ?? snapshot?.DisplayName,
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

        _logger.LogInformation("Setting track offset for {TrackId}: {OffsetMs}ms", trackId, offsetMs);

        await _lyricsCache.SetTrackOffsetAsync(trackId, offsetMs);

        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (!string.IsNullOrEmpty(userId))
        {
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
        else
        {
            await Clients.Caller.ReceiveTrackOffset(new TrackOffsetDto
            {
                TrackId = trackId,
                OffsetMs = offsetMs
            });
        }
    }

    private static string? ResolveSessionId(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        // 1. Cookie
        if (httpContext.Request.Cookies.TryGetValue("cantus_session_id", out string? cookieSessionId) &&
            !string.IsNullOrWhiteSpace(cookieSessionId))
        {
            return cookieSessionId;
        }

        // 2. Query String (access_token or session_id)
        if (httpContext.Request.Query.TryGetValue("access_token", out StringValues queryToken) &&
            !string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken.ToString();
        }

        if (httpContext.Request.Query.TryGetValue("session_id", out StringValues querySessionId) &&
            !string.IsNullOrWhiteSpace(querySessionId))
        {
            return querySessionId.ToString();
        }

        // 3. Authorization Header
        if (httpContext.Request.Headers.TryGetValue("Authorization", out StringValues authHeader) &&
            !string.IsNullOrWhiteSpace(authHeader))
        {
            string headerStr = authHeader.ToString().Trim();
            if (headerStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return headerStr.Substring(7).Trim();
            }
            return headerStr;
        }

        return null;
    }
}
