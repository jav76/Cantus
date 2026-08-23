using Cantus.Core.Interfaces;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

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
        _registry.RegisterConnection(Context.ConnectionId);
        _logger.LogInformation("Client connected to PlaybackHub: {ConnectionId} (Total: {Count})",
            Context.ConnectionId, _registry.ConnectedClientsCount);

        try
        {
            // 1. Send initial active playback snapshot if available
            var snapshot = _registry.GetActivePlaybackSnapshot();
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

            // 2. Send authorized sessions list
            var sessions = await _authService.GetAllSessionsAsync();
            var sessionDtos = sessions.Select(s =>
            {
                var userSnap = _registry.GetUserState(s.Id);
                bool isPlaying = userSnap?.PlaybackState?.IsPlaying ?? false;
                return s.ToDto(isPlaying);
            }).ToList();

            await Clients.Caller.ReceiveSessions(sessionDtos);

            // 3. Send initial diagnostics
            await Clients.Caller.ReceiveDiagnostics(new DiagnosticsDto
            {
                ConnectedClients = _registry.ConnectedClientsCount,
                AuthorizedSessions = sessions.Count,
                PollerStatus = "Running",
                ActiveUserId = snapshot?.UserId,
                ActiveUserName = snapshot?.DisplayName,
                ServerTimeUtc = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending initial state to newly connected client {ConnectionId}", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.UnregisterConnection(Context.ConnectionId);
        _logger.LogInformation("Client disconnected from PlaybackHub: {ConnectionId} (Total: {Count})",
            Context.ConnectionId, _registry.ConnectedClientsCount);

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
        _registry.SetConnectionSubscription(Context.ConnectionId, userId);

        var snapshot = !string.IsNullOrEmpty(userId)
            ? _registry.GetUserState(userId)
            : _registry.GetActivePlaybackSnapshot();

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
    }

    public async Task SetTrackOffset(string trackId, int offsetMs)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return;
        }

        _logger.LogInformation("Setting track offset for {TrackId}: {OffsetMs}ms", trackId, offsetMs);

        await _lyricsCache.SetTrackOffsetAsync(trackId, offsetMs);

        // Update registry if track matches active playback
        var activeSnapshot = _registry.GetActivePlaybackSnapshot();
        if (activeSnapshot?.PlaybackState?.CurrentTrack?.Id == trackId)
        {
            _registry.UpdateUserState(
                activeSnapshot.UserId,
                activeSnapshot.DisplayName,
                activeSnapshot.PlaybackState,
                activeSnapshot.Lyrics,
                offsetMs);
        }

        // Broadcast to all connected clients
        await Clients.All.ReceiveTrackOffset(new TrackOffsetDto
        {
            TrackId = trackId,
            OffsetMs = offsetMs
        });
    }
}
