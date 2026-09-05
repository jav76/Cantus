using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nextPollUtc = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pausedSinceUtc = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _idleSinceUtc = new();
    private readonly ConcurrentDictionary<string, string?> _lastTrackIdByUser = new();

    private CancellationTokenSource _wakeCts = new();
    private DateTimeOffset _rateLimitUntilUtc = DateTimeOffset.MinValue;

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
        _registry.OnUserActivityRequested += (_, userId) => TriggerImmediateUserPoll(userId);
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

    public void TriggerImmediateUserPoll(string userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            _nextPollUtc[userId] = DateTimeOffset.MinValue;
        }

        TriggerImmediatePoll();
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
            _nextPollUtc.Clear();
            _pausedSinceUtc.Clear();
            _idleSinceUtc.Clear();
            _lastTrackIdByUser.Clear();
            return _options.IdlePollIntervalMs;
        }

        // Purge state for disconnected users
        foreach (string trackedId in _nextPollUtc.Keys)
        {
            if (!activeUserIds.Contains(trackedId))
            {
                _nextPollUtc.TryRemove(trackedId, out _);
                _pausedSinceUtc.TryRemove(trackedId, out _);
                _idleSinceUtc.TryRemove(trackedId, out _);
                _lastTrackIdByUser.TryRemove(trackedId, out _);
            }
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

        foreach (string userId in activeUserIds)
        {
            // Skip users whose scheduled poll interval has not yet elapsed
            if (_nextPollUtc.TryGetValue(userId, out DateTimeOffset nextScheduledPoll) && now < nextScheduledPoll)
            {
                continue;
            }

            try
            {
                UserSession? session = await authService.GetSessionAsync(userId, cancellationToken);
                if (session is null)
                {
                    continue;
                }

                bool isVisible = _registry.IsUserVisible(session.Id);
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

                int userIntervalMs;

                if (currentPlayback is not null)
                {
                    _idleSinceUtc.TryRemove(session.Id, out _);

                    if (currentPlayback.IsPlaying)
                    {
                        _pausedSinceUtc.TryRemove(session.Id, out _);

                        // Check if track changed
                        string? currentTrackId = currentPlayback.CurrentTrack?.Id;
                        _lastTrackIdByUser.TryGetValue(session.Id, out string? prevTrackId);
                        bool trackChanged = prevTrackId != currentTrackId;
                        _lastTrackIdByUser[session.Id] = currentTrackId;

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

                        // Calculate dynamic horizon polling interval
                        if (!isVisible)
                        {
                            userIntervalMs = _options.BackgroundPollIntervalMs;
                        }
                        else
                        {
                            TimeSpan duration = currentPlayback.CurrentTrack?.Duration ?? TimeSpan.Zero;
                            TimeSpan progress = currentPlayback.Progress;
                            TimeSpan remaining = duration > progress ? duration - progress : TimeSpan.Zero;

                            if (duration > TimeSpan.Zero)
                            {
                                if (remaining.TotalMilliseconds <= _options.ImminentEndThresholdMs)
                                {
                                    userIntervalMs = _options.ImminentEndPollIntervalMs;
                                }
                                else if (remaining.TotalMilliseconds <= _options.ApproachingEndThresholdMs)
                                {
                                    userIntervalMs = _options.ApproachingEndPollIntervalMs;
                                }
                                else
                                {
                                    userIntervalMs = _options.ActivePollIntervalMs;
                                }
                            }
                            else
                            {
                                userIntervalMs = _options.ActivePollIntervalMs;
                            }
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
                        string status = isVisible ? "Active (Playing)" : "Active (Background)";
                        await _hubContext.Clients.Group(userGroup).ReceiveDiagnostics(new DiagnosticsDto
                        {
                            ConnectedClients = _registry.ConnectedClientsCount,
                            AuthorizedSessions = 1,
                            PollerStatus = status,
                            ActivePollIntervalMs = userIntervalMs,
                            ActiveUserId = session.Id,
                            ActiveUserName = session.DisplayName,
                            ServerTimeUtc = DateTimeOffset.UtcNow
                        });
                    }
                    else
                    {
                        // Paused state: calculate graduated backoff
                        DateTimeOffset pausedSince = _pausedSinceUtc.GetOrAdd(session.Id, now);
                        TimeSpan pausedDuration = now - pausedSince;

                        if (!isVisible)
                        {
                            userIntervalMs = Math.Max(_options.PausedDeepPollIntervalMs, _options.BackgroundPollIntervalMs);
                        }
                        else if (pausedDuration > TimeSpan.FromMinutes(5))
                        {
                            userIntervalMs = _options.PausedDeepPollIntervalMs;
                        }
                        else if (pausedDuration > TimeSpan.FromMinutes(1))
                        {
                            userIntervalMs = _options.PausedExtendedPollIntervalMs;
                        }
                        else
                        {
                            userIntervalMs = _options.PausedPollIntervalMs;
                        }

                        _registry.UpdateUserState(
                            session.Id,
                            session.DisplayName,
                            currentPlayback,
                            previousSnapshot?.Lyrics,
                            previousSnapshot?.TrackOffsetMs ?? 0);

                        await _hubContext.Clients.Group(userGroup).ReceivePlaybackState(
                            currentPlayback.ToDto(session.Id, session.DisplayName));

                        await _hubContext.Clients.Group(userGroup).ReceiveDiagnostics(new DiagnosticsDto
                        {
                            ConnectedClients = _registry.ConnectedClientsCount,
                            AuthorizedSessions = 1,
                            PollerStatus = "Paused",
                            ActivePollIntervalMs = userIntervalMs,
                            ActiveUserId = session.Id,
                            ActiveUserName = session.DisplayName,
                            ServerTimeUtc = DateTimeOffset.UtcNow
                        });
                    }
                }
                else
                {
                    // Idle state: calculate graduated backoff
                    _pausedSinceUtc.TryRemove(session.Id, out _);
                    _lastTrackIdByUser.TryRemove(session.Id, out _);

                    DateTimeOffset idleSince = _idleSinceUtc.GetOrAdd(session.Id, now);
                    TimeSpan idleDuration = now - idleSince;

                    if (!isVisible)
                    {
                        userIntervalMs = Math.Max(_options.IdleDeepPollIntervalMs, _options.BackgroundPollIntervalMs);
                    }
                    else if (idleDuration > TimeSpan.FromMinutes(10))
                    {
                        userIntervalMs = _options.IdleDeepPollIntervalMs;
                    }
                    else if (idleDuration > TimeSpan.FromMinutes(2))
                    {
                        userIntervalMs = _options.IdleExtendedPollIntervalMs;
                    }
                    else
                    {
                        userIntervalMs = _options.IdlePollIntervalMs;
                    }

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
                        ActivePollIntervalMs = userIntervalMs,
                        ActiveUserId = session.Id,
                        ActiveUserName = session.DisplayName,
                        ServerTimeUtc = DateTimeOffset.UtcNow
                    });
                }

                _nextPollUtc[session.Id] = DateTimeOffset.UtcNow.AddMilliseconds(userIntervalMs);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error polling Spotify playback for user {UserId}", userId);
                _nextPollUtc[userId] = DateTimeOffset.UtcNow.AddMilliseconds(_options.PausedPollIntervalMs);
            }
        }

        DateTimeOffset earliestNext = DateTimeOffset.MaxValue;
        foreach (string uid in activeUserIds)
        {
            if (_nextPollUtc.TryGetValue(uid, out DateTimeOffset t))
            {
                if (t < earliestNext)
                {
                    earliestNext = t;
                }
            }
        }

        if (earliestNext == DateTimeOffset.MaxValue)
        {
            return _options.ActivePollIntervalMs;
        }

        TimeSpan waitTime = earliestNext - DateTimeOffset.UtcNow;
        return (int)Math.Clamp(waitTime.TotalMilliseconds, 500, 10000);
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
