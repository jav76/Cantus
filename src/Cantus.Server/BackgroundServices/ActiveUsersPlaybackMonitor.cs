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
using SpotifyAPI.Web;

namespace Cantus.Server.BackgroundServices;

public sealed class ActiveUsersPlaybackMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybackSessionRegistry _registry;
    private readonly IHubContext<PlaybackHub, IPlaybackClient> _hubContext;
    private readonly PlaybackPollerOptions _options;
    private readonly ILogger<ActiveUsersPlaybackMonitor> _logger;
    private readonly object _wakeLock = new();
    private CancellationTokenSource _wakeCts = new();
    private DateTimeOffset _rateLimitUntilUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDiagnosticsBroadcast = DateTimeOffset.MinValue;

    public DateTimeOffset RateLimitUntilUtc => _rateLimitUntilUtc;
    public bool IsRateLimited => DateTimeOffset.UtcNow < _rateLimitUntilUtc;

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
        lock (_wakeLock)
        {
            if (!_wakeCts.IsCancellationRequested)
            {
                try
                {
                    _wakeCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
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
                CancellationTokenSource currentWakeCts;
                lock (_wakeLock)
                {
                    if (_wakeCts.IsCancellationRequested)
                    {
                        _wakeCts.Dispose();
                        _wakeCts = new();
                    }
                    currentWakeCts = _wakeCts;
                }

                // If zero connected clients, wait for someone to connect
                if (!_registry.HasConnectedClients)
                {
                    try
                    {
                        using CancellationTokenSource linkedZeroClients =
                            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, currentWakeCts.Token);
                        await Task.Delay(1000, linkedZeroClients.Token);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                    }
                    continue;
                }

                int pollDelayMs = await PollActiveSessionsAsync(stoppingToken);

                // Delay according to adaptive rate or immediate wake signal
                try
                {
                    using CancellationTokenSource linkedDelay =
                        CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, currentWakeCts.Token);
                    await Task.Delay(pollDelayMs, linkedDelay.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
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
        using IServiceScope scope = _scopeFactory.CreateScope();
        ISpotifyAuthService authService = scope.ServiceProvider.GetRequiredService<ISpotifyAuthService>();
        ISpotifyPlayerClient spotifyClient = scope.ServiceProvider.GetRequiredService<ISpotifyPlayerClient>();
        ILyricsProvider lyricsProvider = scope.ServiceProvider.GetRequiredService<ILyricsProvider>();
        ILyricsCacheRepository lyricsCache = scope.ServiceProvider.GetRequiredService<ILyricsCacheRepository>();

        IReadOnlySet<string> activeUserIds = _registry.GetActiveUserIdsWithConnectedClients();

        if (activeUserIds.Count == 0)
        {
            return _options.IdlePollIntervalMs;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < _rateLimitUntilUtc)
        {
            TimeSpan remaining = _rateLimitUntilUtc - now;
            string rateLimitStatus = $"Rate Limited ({FormatTimeSpan(remaining)})";

            foreach (string userId in activeUserIds)
            {
                UserSession? session = await authService.GetSessionAsync(userId, cancellationToken);
                if (session is null)
                {
                    continue;
                }

                string userGroup = $"user_{session.Id}";
                await _hubContext.Clients.Group(userGroup).ReceiveDiagnostics(new DiagnosticsDto
                {
                    ConnectedClients = _registry.ConnectedClientsCount,
                    AuthorizedSessions = 1,
                    PollerStatus = rateLimitStatus,
                    ActivePollIntervalMs = (int)Math.Min(remaining.TotalMilliseconds, 10000),
                    ActiveUserId = session.Id,
                    ActiveUserName = session.DisplayName,
                    ServerTimeUtc = DateTimeOffset.UtcNow
                });
            }

            return (int)Math.Clamp(remaining.TotalMilliseconds, 1000, 10000);
        }

        bool anyPlaying = false;
        bool anyActive = false;

        foreach (string userId in activeUserIds)
        {
            try
            {
                UserSession? session = await authService.GetSessionAsync(userId, cancellationToken);
                if (session is null)
                {
                    continue;
                }

                UserPlaybackSnapshot? previousSnapshot = _registry.GetUserState(session.Id);
                PlaybackState? currentPlayback = null;
                string userGroup = $"user_{session.Id}";

                try
                {
                    currentPlayback = await spotifyClient.GetCurrentPlaybackAsync(
                        session.AccessToken,
                        cancellationToken);
                }
                catch (APITooManyRequestsException tooManyEx)
                {
                    TimeSpan retryAfter = tooManyEx.RetryAfter > TimeSpan.Zero
                        ? tooManyEx.RetryAfter
                        : TimeSpan.FromSeconds(60);
                    _rateLimitUntilUtc = DateTimeOffset.UtcNow.Add(retryAfter);
                    _logger.LogWarning(
                        "Spotify API rate limit hit for user {UserId}. Pausing Spotify polling until {RateLimitUntil} UTC (Retry after {RetryAfter})",
                        session.Id,
                        _rateLimitUntilUtc,
                        retryAfter);

                    string rateLimitStatus = $"Rate Limited ({FormatTimeSpan(retryAfter)})";
                    await _hubContext.Clients.Group(userGroup).ReceiveDiagnostics(new DiagnosticsDto
                    {
                        ConnectedClients = _registry.ConnectedClientsCount,
                        AuthorizedSessions = 1,
                        PollerStatus = rateLimitStatus,
                        ActivePollIntervalMs = (int)Math.Min(retryAfter.TotalMilliseconds, 10000),
                        ActiveUserId = session.Id,
                        ActiveUserName = session.DisplayName,
                        ServerTimeUtc = DateTimeOffset.UtcNow
                    });

                    return (int)Math.Clamp(retryAfter.TotalMilliseconds, 1000, 10000);
                }
                catch (Exception ex) when (
                    ex is APIUnauthorizedException ||
                    ex.Message.Contains("401") ||
                    ex.Message.Contains("Unauthorized") ||
                    ex.GetType().Name.Contains("Unauthorized"))
                {
                    _logger.LogWarning("Spotify token expired for user {UserId}. Refreshing...", session.Id);
                    try
                    {
                        UserSession refreshedSession = await authService.RefreshTokenAsync(
                            session.Id,
                            cancellationToken);
                        currentPlayback = await spotifyClient.GetCurrentPlaybackAsync(
                            refreshedSession.AccessToken,
                            cancellationToken);
                    }
                    catch (APITooManyRequestsException refreshTooManyEx)
                    {
                        TimeSpan retryAfter = refreshTooManyEx.RetryAfter > TimeSpan.Zero
                            ? refreshTooManyEx.RetryAfter
                            : TimeSpan.FromSeconds(60);
                        _rateLimitUntilUtc = DateTimeOffset.UtcNow.Add(retryAfter);
                        _logger.LogWarning(
                            "Spotify API rate limit hit during token refresh for user {UserId}. Pausing Spotify polling until {RateLimitUntil} UTC (Retry after {RetryAfter})",
                            session.Id,
                            _rateLimitUntilUtc,
                            retryAfter);

                        string rateLimitStatus = $"Rate Limited ({FormatTimeSpan(retryAfter)})";
                        await _hubContext.Clients.Group(userGroup).ReceiveDiagnostics(new DiagnosticsDto
                        {
                            ConnectedClients = _registry.ConnectedClientsCount,
                            AuthorizedSessions = 1,
                            PollerStatus = rateLimitStatus,
                            ActivePollIntervalMs = (int)Math.Min(retryAfter.TotalMilliseconds, 10000),
                            ActiveUserId = session.Id,
                            ActiveUserName = session.DisplayName,
                            ServerTimeUtc = DateTimeOffset.UtcNow
                        });

                        return (int)Math.Clamp(retryAfter.TotalMilliseconds, 1000, 10000);
                    }
                    catch (Exception refreshEx)
                    {
                        _logger.LogError(refreshEx, "Failed to refresh token for user {UserId}", session.Id);
                    }
                }

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
                        _logger.LogInformation(
                            "Track changed for user {DisplayName}: {Artist} - {Title}",
                            session.DisplayName,
                            currentPlayback.CurrentTrack.Artist,
                            currentPlayback.CurrentTrack.Title);

                        lyrics = await lyricsProvider.GetLyricsAsync(
                            currentPlayback.CurrentTrack,
                            cancellationToken);
                        trackOffset = await lyricsCache.GetTrackOffsetAsync(
                            currentPlayback.CurrentTrack.Id,
                            cancellationToken);
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
                        else if (currentPlayback.CurrentTrack is not null)
                        {
                            await _hubContext.Clients.Group(userGroup).ReceiveLyrics(new LyricsDto
                            {
                                TrackId = currentPlayback.CurrentTrack.Id,
                                Title = currentPlayback.CurrentTrack.Title,
                                Artist = currentPlayback.CurrentTrack.Artist,
                                Album = currentPlayback.CurrentTrack.Album,
                                Lines = [],
                                IsSynced = false,
                                IsInstrumental = false,
                                PlainLyrics = null
                            });
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
                        ActivePollIntervalMs = currentPlayback.IsPlaying
                            ? _options.ActivePollIntervalMs
                            : _options.PausedPollIntervalMs,
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

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public override void Dispose()
    {
        lock (_wakeLock)
        {
            _wakeCts.Dispose();
        }
        base.Dispose();
    }
}
