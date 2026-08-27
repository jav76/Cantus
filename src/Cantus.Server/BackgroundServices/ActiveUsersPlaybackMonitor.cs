using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Server.BackgroundServices;

public sealed class ActiveUsersPlaybackMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybackSessionRegistry _registry;
    private readonly IHubContext<PlaybackHub, IPlaybackClient> _hubContext;
    private readonly PlaybackPollerOptions _options;
    private readonly ILogger<ActiveUsersPlaybackMonitor> _logger;
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private DateTimeOffset _lastDiagnosticsBroadcast = DateTimeOffset.MinValue;

    public ActiveUsersPlaybackMonitor(
        IServiceScopeFactory scopeFactory,
        IPlaybackSessionRegistry registry,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        IOptions<PlaybackPollerOptions> options,
        ILogger<ActiveUsersPlaybackMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;

        _registry.OnClientsConnected += (_, _) => TriggerImmediatePoll();
        _registry.OnSessionsChanged += (_, _) => TriggerImmediatePoll();
    }

    public void TriggerImmediatePoll()
    {
        if (_wakeSignal.CurrentCount == 0)
        {
            try
            {
                _wakeSignal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ActiveUsersPlaybackMonitor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // If zero connected clients, wait for someone to connect
                if (!_registry.HasConnectedClients)
                {
                    try
                    {
                        await Task.WhenAny(
                            Task.Delay(1000, stoppingToken),
                            _wakeSignal.WaitAsync(stoppingToken));
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    continue;
                }

                int pollDelayMs = await PollActiveSessionsAsync(stoppingToken);

                // Delay according to adaptive rate or immediate wake signal
                try
                {
                    await Task.WhenAny(
                        Task.Delay(pollDelayMs, stoppingToken),
                        _wakeSignal.WaitAsync(stoppingToken));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ActiveUsersPlaybackMonitor poller loop.");
                await Task.Delay(3000, stoppingToken);
            }
        }

        _logger.LogInformation("ActiveUsersPlaybackMonitor stopped.");
    }

    private async Task<int> PollActiveSessionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<ISpotifyAuthService>();
        var spotifyClient = scope.ServiceProvider.GetRequiredService<ISpotifyPlayerClient>();
        var lyricsProvider = scope.ServiceProvider.GetRequiredService<ILyricsProvider>();
        var lyricsCache = scope.ServiceProvider.GetRequiredService<ILyricsCacheRepository>();

        var activeUserIds = _registry.GetActiveUserIdsWithConnectedClients();

        if (activeUserIds.Count == 0)
        {
            return _options.IdlePollIntervalMs;
        }

        bool anyPlaying = false;
        bool anyActive = false;

        foreach (var userId in activeUserIds)
        {
            try
            {
                var session = await authService.GetSessionAsync(userId, cancellationToken);
                if (session is null)
                {
                    continue;
                }

                var previousSnapshot = _registry.GetUserState(session.Id);
                PlaybackState? currentPlayback = null;

                try
                {
                    currentPlayback = await spotifyClient.GetCurrentPlaybackAsync(session.AccessToken, cancellationToken);
                }
                catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized") || ex.GetType().Name.Contains("Unauthorized"))
                {
                    _logger.LogWarning("Spotify token expired for user {UserId}. Refreshing...", session.Id);
                    try
                    {
                        var refreshedSession = await authService.RefreshTokenAsync(session.Id, cancellationToken);
                        currentPlayback = await spotifyClient.GetCurrentPlaybackAsync(refreshedSession.AccessToken, cancellationToken);
                    }
                    catch (Exception refreshEx)
                    {
                        _logger.LogError(refreshEx, "Failed to refresh token for user {UserId}", session.Id);
                    }
                }

                string userGroup = $"user_{session.Id}";

                if (currentPlayback is not null)
                {
                    anyActive = true;
                    if (currentPlayback.IsPlaying)
                    {
                        anyPlaying = true;
                    }

                    // Check if track changed
                    string? prevTrackId = previousSnapshot?.PlaybackState?.CurrentTrack?.Id;
                    string? newTrackId = currentPlayback.CurrentTrack?.Id;

                    bool trackChanged = prevTrackId != newTrackId;
                    SyncedLyrics? lyrics = previousSnapshot?.Lyrics;
                    int trackOffset = previousSnapshot?.TrackOffsetMs ?? 0;

                    if (trackChanged && currentPlayback.CurrentTrack is not null)
                    {
                        _logger.LogInformation("Track changed for user {DisplayName}: {Artist} - {Title}",
                            session.DisplayName, currentPlayback.CurrentTrack.Artist, currentPlayback.CurrentTrack.Title);

                        lyrics = await lyricsProvider.GetLyricsAsync(currentPlayback.CurrentTrack, cancellationToken);
                        trackOffset = await lyricsCache.GetTrackOffsetAsync(currentPlayback.CurrentTrack.Id, cancellationToken);
                    }

                    // Update in-memory registry
                    _registry.UpdateUserState(
                        session.Id,
                        session.DisplayName,
                        currentPlayback,
                        lyrics,
                        trackOffset);

                    // Broadcast exclusively to this user's SignalR group
                    if (trackChanged)
                    {
                        if (lyrics is not null)
                        {
                            await _hubContext.Clients.Group(userGroup).ReceiveLyrics(lyrics.ToDto());
                        }

                        if (currentPlayback.CurrentTrack is not null)
                        {
                            await _hubContext.Clients.Group(userGroup).ReceiveTrackOffset(new TrackOffsetDto
                            {
                                TrackId = currentPlayback.CurrentTrack.Id,
                                OffsetMs = trackOffset
                            });
                        }
                    }

                    // Broadcast playback state to user group
                    await _hubContext.Clients.Group(userGroup).ReceivePlaybackState(
                        currentPlayback.ToDto(session.Id, session.DisplayName));

                    // Diagnostics for user group
                    string status = currentPlayback.IsPlaying ? "Active (Playing)" : "Paused";
                    await _hubContext.Clients.Group(userGroup).ReceiveDiagnostics(new DiagnosticsDto
                    {
                        ConnectedClients = _registry.ConnectedClientsCount,
                        AuthorizedSessions = 1,
                        PollerStatus = status,
                        ActivePollIntervalMs = currentPlayback.IsPlaying ? _options.ActivePollIntervalMs : _options.PausedPollIntervalMs,
                        ActiveUserId = session.Id,
                        ActiveUserName = session.DisplayName,
                        ServerTimeUtc = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    // No current playback for this user
                    _registry.UpdateUserState(
                        session.Id,
                        session.DisplayName,
                        null,
                        null,
                        0);

                    await _hubContext.Clients.Group(userGroup).ReceiveDiagnostics(new DiagnosticsDto
                    {
                        ConnectedClients = _registry.ConnectedClientsCount,
                        AuthorizedSessions = 1,
                        PollerStatus = "Idle",
                        ActivePollIntervalMs = _options.IdlePollIntervalMs,
                        ActiveUserId = session.Id,
                        ActiveUserName = session.DisplayName,
                        ServerTimeUtc = DateTimeOffset.UtcNow
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error polling Spotify playback for user {UserId}", userId);
            }
        }

        if (anyPlaying)
        {
            return _options.ActivePollIntervalMs;
        }

        if (anyActive)
        {
            return _options.PausedPollIntervalMs;
        }

        return _options.IdlePollIntervalMs;
    }
}
