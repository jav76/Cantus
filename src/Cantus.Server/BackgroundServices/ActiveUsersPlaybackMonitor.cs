using System.Collections.Concurrent;
using System.Net;
using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
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
    private readonly PollingCadencePolicy _cadence;
    private readonly ILogger<ActiveUsersPlaybackMonitor> _logger;
    private readonly SpotifyRateLimitGate _rateLimitGate;
    private readonly object _wakeLock = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nextPollUtc = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pausedSinceUtc = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _idleSinceUtc = new();
    private readonly ConcurrentDictionary<string, string?> _lastTrackIdByUser = new();

    private CancellationTokenSource _wakeCts = new();

    public DateTimeOffset RateLimitUntilUtc => _rateLimitGate.LimitedUntilUtc;
    public bool IsRateLimited => _rateLimitGate.IsLimited;

    public ActiveUsersPlaybackMonitor(
        IServiceScopeFactory scopeFactory,
        IPlaybackSessionRegistry registry,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        IOptions<PlaybackPollerOptions> options,
        ILogger<ActiveUsersPlaybackMonitor> logger,
        SpotifyRateLimitGate? rateLimitGate = null)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _hubContext = hubContext;
        _cadence = new PollingCadencePolicy(options.Value);
        _logger = logger;
        _rateLimitGate = rateLimitGate ?? new(TimeProvider.System);

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
                        await Task.Delay(PollingCadencePolicy.NO_CONNECTED_CLIENTS_DELAY_MS, linkedZeroClients.Token);
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
                await Task.Delay(PollingCadencePolicy.LOOP_ERROR_DELAY_MS, stoppingToken);
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
            return _cadence.NoActiveUsersDelayMs;
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
        TimeSpan rateLimitRemaining = _rateLimitGate.Remaining;
        if (rateLimitRemaining > TimeSpan.Zero)
        {
            string rateLimitStatus = $"Rate Limited ({FormatTimeSpan(rateLimitRemaining)})";

            foreach (string userId in activeUserIds)
            {
                UserSession? session = await authService.GetSessionForUserAsync(userId, cancellationToken);
                if (session is null)
                {
                    continue;
                }

                await _hubContext.Clients.Viewers(session.Id).ReceiveDiagnostics(new DiagnosticsDto
                {
                    ConnectedClients = _registry.ConnectedClientsCount,
                    AuthorizedSessions = 1,
                    PollerStatus = rateLimitStatus,
                    ActivePollIntervalMs = _cadence.GetRateLimitedReportedIntervalMs(rateLimitRemaining),
                    ActiveUserId = session.Id,
                    ActiveUserName = session.DisplayName,
                    ServerTimeUtc = DateTimeOffset.UtcNow
                });
            }

            return _cadence.GetRateLimitedLoopDelayMs(rateLimitRemaining);
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
                UserSession? session = await authService.GetSessionForUserAsync(userId, cancellationToken);
                if (session is null)
                {
                    continue;
                }

                bool isVisible = _registry.IsUserVisible(session.Id);
                UserPlaybackSnapshot? previousSnapshot = _registry.GetUserState(session.Id);
                PlaybackState? currentPlayback = null;

                try
                {
                    currentPlayback = await spotifyClient.GetCurrentPlaybackAsync(
                        session.AccessToken,
                        cancellationToken);
                }
                catch (APITooManyRequestsException tooManyEx)
                {
                    TimeSpan retryAfter = _rateLimitGate.Report(tooManyEx.RetryAfter);
                    _logger.LogWarning(
                        "Spotify API rate limit hit for Spotify user {SpotifyUserId} (Session: {SessionRef}). Pausing Spotify polling until {RateLimitUntil} UTC (Retry after {RetryAfter})",
                        session.SpotifyUserId,
                        SessionLogRef.For(session.Id),
                        _rateLimitGate.LimitedUntilUtc,
                        retryAfter);

                    string rateLimitStatus = $"Rate Limited ({FormatTimeSpan(retryAfter)})";
                    await _hubContext.Clients.Viewers(session.Id).ReceiveDiagnostics(new DiagnosticsDto
                    {
                        ConnectedClients = _registry.ConnectedClientsCount,
                        AuthorizedSessions = 1,
                        PollerStatus = rateLimitStatus,
                        ActivePollIntervalMs = _cadence.GetRateLimitedReportedIntervalMs(retryAfter),
                        ActiveUserId = session.Id,
                        ActiveUserName = session.DisplayName,
                        ServerTimeUtc = DateTimeOffset.UtcNow
                    });

                    return _cadence.GetRateLimitedLoopDelayMs(retryAfter);
                }
                catch (Exception ex) when (IsUnauthorized(ex))
                {
                    _logger.LogWarning(
                        "Spotify token expired for Spotify user {SpotifyUserId} (Session: {SessionRef}). Refreshing...",
                        session.SpotifyUserId,
                        SessionLogRef.For(session.Id));
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
                        TimeSpan retryAfter = _rateLimitGate.Report(refreshTooManyEx.RetryAfter);
                        _logger.LogWarning(
                            "Spotify API rate limit hit during token refresh for Spotify user {SpotifyUserId} (Session: {SessionRef}). Pausing Spotify polling until {RateLimitUntil} UTC (Retry after {RetryAfter})",
                            session.SpotifyUserId,
                            SessionLogRef.For(session.Id),
                            _rateLimitGate.LimitedUntilUtc,
                            retryAfter);

                        string rateLimitStatus = $"Rate Limited ({FormatTimeSpan(retryAfter)})";
                        await _hubContext.Clients.Viewers(session.Id).ReceiveDiagnostics(new DiagnosticsDto
                        {
                            ConnectedClients = _registry.ConnectedClientsCount,
                            AuthorizedSessions = 1,
                            PollerStatus = rateLimitStatus,
                            ActivePollIntervalMs = _cadence.GetRateLimitedReportedIntervalMs(retryAfter),
                            ActiveUserId = session.Id,
                            ActiveUserName = session.DisplayName,
                            ServerTimeUtc = DateTimeOffset.UtcNow
                        });

                        return _cadence.GetRateLimitedLoopDelayMs(retryAfter);
                    }
                    catch (Exception refreshEx)
                    {
                        _logger.LogError(
                            refreshEx,
                            "Failed to refresh token for Spotify user {SpotifyUserId} (Session: {SessionRef})",
                            session.SpotifyUserId,
                            SessionLogRef.For(session.Id));
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

                        userIntervalMs = _cadence.GetUserPollIntervalMs(currentPlayback, isVisible, TimeSpan.Zero);

                        // Update in-memory registry. On the same track the
                        // snapshot's lyrics and offset are kept as they are at
                        // write time, so an override applied during the
                        // Spotify call above is not reverted.
                        if (trackChanged)
                        {
                            _registry.UpdateUserState(
                                session.Id,
                                session.DisplayName,
                                currentPlayback,
                                lyrics,
                                trackOffset);
                        }
                        else
                        {
                            _registry.UpdateUserPlayback(session.Id, session.DisplayName, currentPlayback);
                        }

                        // Broadcast exclusively to this user's SignalR group
                        if (trackChanged)
                        {
                            if (lyrics is not null)
                            {
                                await _hubContext.Clients.Viewers(session.Id).ReceiveLyrics(lyrics.ToDto());
                            }
                            else if (currentPlayback.CurrentTrack is not null)
                            {
                                await _hubContext.Clients.Viewers(session.Id).ReceiveLyrics(new LyricsDto
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
                                await _hubContext.Clients.Viewers(session.Id).ReceiveTrackOffset(new TrackOffsetDto
                                {
                                    TrackId = currentPlayback.CurrentTrack.Id,
                                    OffsetMs = trackOffset
                                });
                            }
                        }

                        // Broadcast playback state to user group
                        await _hubContext.Clients.Viewers(session.Id).ReceivePlaybackState(
                            currentPlayback.ToDto(session.Id, session.DisplayName));

                        // Diagnostics for user group
                        string status = isVisible ? "Active (Playing)" : "Active (Background)";
                        await _hubContext.Clients.Viewers(session.Id).ReceiveDiagnostics(new DiagnosticsDto
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

                        userIntervalMs = _cadence.GetUserPollIntervalMs(currentPlayback, isVisible, pausedDuration);

                        _registry.UpdateUserPlayback(session.Id, session.DisplayName, currentPlayback);

                        await _hubContext.Clients.Viewers(session.Id).ReceivePlaybackState(
                            currentPlayback.ToDto(session.Id, session.DisplayName));

                        await _hubContext.Clients.Viewers(session.Id).ReceiveDiagnostics(new DiagnosticsDto
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

                    userIntervalMs = _cadence.GetUserPollIntervalMs(null, isVisible, idleDuration);

                    _registry.UpdateUserState(
                        session.Id,
                        session.DisplayName,
                        null,
                        null,
                        0);

                    await _hubContext.Clients.Viewers(session.Id).ReceiveDiagnostics(new DiagnosticsDto
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
                _logger.LogError(ex, "Error polling Spotify playback for Session {SessionRef}", SessionLogRef.For(userId));
                _nextPollUtc[userId] = DateTimeOffset.UtcNow.AddMilliseconds(_cadence.PollErrorIntervalMs);
            }
        }

        DateTimeOffset? earliestNext = null;
        foreach (string uid in activeUserIds)
        {
            if (_nextPollUtc.TryGetValue(uid, out DateTimeOffset t) && (earliestNext is null || t < earliestNext))
            {
                earliestNext = t;
            }
        }

        return _cadence.GetLoopDelayMs(earliestNext, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// A 401 from Spotify means the access token expired and the session
    /// should be refreshed. This matches on the typed exception, plus the
    /// status code of a plain APIException as a fallback, rather than on
    /// exception text: the previous check treated any failure whose message
    /// merely contained "401" or "Unauthorized" as an expired token, so an
    /// unrelated error could trigger a needless token refresh.
    /// </summary>
    private static bool IsUnauthorized(Exception exception)
    {
        return exception is APIUnauthorizedException
            || (exception is APIException apiException
                && apiException.Response?.StatusCode == HttpStatusCode.Unauthorized);
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
