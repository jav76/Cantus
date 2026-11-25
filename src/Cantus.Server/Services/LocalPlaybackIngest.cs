using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Channels;
using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Sessions;
using Cantus.Server.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Services;

/// <summary>The result of a publish, sent to the client as an int.</summary>
public enum LocalPublishResult
{
    Accepted = 0,
    NotLocalSession = 1,
    NotPublisher = 2,
    RateLimited = 3,
    Invalid = 4
}

public interface ILocalPlaybackIngest
{
    /// <summary>
    /// Validates, rate-limits and queues one publish. Never waits on a lyrics fetch, because
    /// SignalR runs one invocation per connection at a time and the client's clock sync shares it.
    /// </summary>
    LocalPublishResult Submit(string connectionId, UserSession session, LocalPlaybackUpdateDto? update);

    /// <summary>Drops a closed connection's state and releases its publisher claim.</summary>
    void ReleaseConnection(string connectionId);

    /// <summary>
    /// Drops everything held for a Session that ended, refuses publishes for it that were already
    /// in flight, and completes once its worker has stopped, so a caller that then tells clients
    /// the Session ended cannot be overtaken by a broadcast from an apply that was still running.
    /// </summary>
    Task ForgetAsync(string sessionId);

    /// <summary>
    /// True once a Session's publisher left and the grace period ended with no new one, until a
    /// connection publishes again.
    /// </summary>
    bool IsPublisherOffline(string sessionId);
}

/// <summary>
/// Takes playback that Local Sessions publish and applies it in the background, one Session at
/// a time. Each Session has one publisher connection, a slot that keeps only the newest update,
/// and a lyric budget; each connection has a token bucket and a sequence check. A track that
/// arrives without a cover gets the one the catalogue has for it: from the cache before the
/// broadcast, or in a later broadcast once <see cref="ICoverArtBackfill"/> has found it.
/// </summary>
/// <remarks>
/// The newest connection of a Session to publish takes the claim, because one Session is one
/// person: a client that reconnects after a network change must not be locked out by its old
/// connection, which the server may keep for up to the client timeout. A connection whose claim
/// was taken gets <see cref="LocalPublishResult.NotPublisher"/> until the claim is free again.
/// </remarks>
public sealed class LocalPlaybackIngest : ILocalPlaybackIngest, IDisposable
{
    public const int TOKEN_BUCKET_BURST = 10;
    public const double TOKEN_REFILL_PER_SECOND = 2;
    public const int LYRIC_FETCHES_PER_MINUTE = 20;
    public const int MAX_PLAYER_FIELD_LENGTH = 128;
    public const int MAX_TRACK_FIELD_LENGTH = LyricMatchConstants.MAX_NAME_CHARACTERS;
    public const int MAX_ALBUM_ART_URL_LENGTH = 2048;
    public const long MIN_DURATION_MS = 30_000;
    public const long MAX_DURATION_MS = 1_200_000;
    public const double MIN_RATE = 0.25;
    public const double MAX_RATE = 4.0;

    private static readonly TimeSpan TimestampPastTolerance = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TimestampFutureTolerance = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LyricBudgetWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TouchInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ForgottenSessionRetention = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WorkerStopTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybackUpdateApplier _applier;
    private readonly IPlaybackSessionRegistry _registry;
    private readonly ICoverArtBackfill _coverArtBackfill;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<LocalSessionOptions> _options;
    private readonly ILogger<LocalPlaybackIngest> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, ConnectionState> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionPipeline> _pipelines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _forgottenSessions = new(StringComparer.Ordinal);
    private bool _disposed;

    public LocalPlaybackIngest(
        IServiceScopeFactory scopeFactory,
        IPlaybackUpdateApplier applier,
        IPlaybackSessionRegistry registry,
        ICoverArtBackfill coverArtBackfill,
        TimeProvider timeProvider,
        IOptions<LocalSessionOptions> options,
        ILogger<LocalPlaybackIngest> logger)
    {
        _scopeFactory = scopeFactory;
        _applier = applier;
        _registry = registry;
        _coverArtBackfill = coverArtBackfill;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
        _coverArtBackfill.Resolved += OnCoverArtResolved;
    }

    public LocalPublishResult Submit(string connectionId, UserSession session, LocalPlaybackUpdateDto? update)
    {
        if (session.Kind != SessionKind.Local || !_options.Value.Enabled)
        {
            return LocalPublishResult.NotLocalSession;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_forgottenSessions.ContainsKey(session.Id))
            {
                return LocalPublishResult.NotLocalSession;
            }

            // The hub looks the Session up before calling here, and SignalR does not wait for
            // that before running OnDisconnectedAsync. The registry drops a connection before
            // ReleaseConnection runs, so this refuses a publish that would outlive its release.
            if (!_registry.IsConnected(connectionId))
            {
                return LocalPublishResult.NotPublisher;
            }

            ConnectionState connection = GetOrCreateConnection(connectionId, session.Id, now);
            if (!connection.TryTakeToken(now))
            {
                return LocalPublishResult.RateLimited;
            }

            if (!TryBuildState(update, now, out PlaybackState? state, out string? claimedSpotifyTrackId))
            {
                return LocalPublishResult.Invalid;
            }

            SessionPipeline pipeline = GetOrCreatePipeline(session.Id);
            if (!TryHoldClaim(pipeline, connectionId, connection))
            {
                return LocalPublishResult.NotPublisher;
            }

            if (update.Sequence <= connection.LastSequence)
            {
                return LocalPublishResult.Accepted;
            }

            connection.LastSequence = update.Sequence;
            pipeline.DisplayName = session.DisplayName;
            pipeline.PendingApply = new ApplyItem(state, session.DisplayName, claimedSpotifyTrackId);
            pipeline.Signal();
            return LocalPublishResult.Accepted;
        }
    }

    public void ReleaseConnection(string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.Remove(connectionId, out ConnectionState? connection))
            {
                return;
            }

            if (_pipelines.TryGetValue(connection.SessionId, out SessionPipeline? pipeline)
                && string.Equals(pipeline.PublisherConnectionId, connectionId, StringComparison.Ordinal))
            {
                pipeline.PublisherConnectionId = null;
                StartGraceTimer(pipeline);
            }
        }
    }

    public async Task ForgetAsync(string sessionId)
    {
        Task worker = Task.CompletedTask;
        lock (_lock)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (KeyValuePair<string, DateTimeOffset> entry in _forgottenSessions.ToList())
            {
                if (now - entry.Value >= ForgottenSessionRetention)
                {
                    _forgottenSessions.Remove(entry.Key);
                }
            }

            _forgottenSessions[sessionId] = now;

            foreach (KeyValuePair<string, ConnectionState> entry in _connections.ToList())
            {
                if (string.Equals(entry.Value.SessionId, sessionId, StringComparison.Ordinal))
                {
                    _connections.Remove(entry.Key);
                }
            }

            if (_pipelines.Remove(sessionId, out SessionPipeline? pipeline))
            {
                pipeline.Ended = true;
                pipeline.Stop();
                worker = pipeline.Worker;
            }
        }

        try
        {
            // Real time on purpose: a fake clock in tests would otherwise never end the wait.
            await worker.WaitAsync(WorkerStopTimeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Worker for ended Session {SessionRef} did not stop within {Timeout}",
                SessionLogRef.For(sessionId),
                WorkerStopTimeout);
        }
    }

    public bool IsPublisherOffline(string sessionId)
    {
        lock (_lock)
        {
            return _pipelines.TryGetValue(sessionId, out SessionPipeline? pipeline) && pipeline.PublisherOffline;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _coverArtBackfill.Resolved -= OnCoverArtResolved;
            foreach (SessionPipeline pipeline in _pipelines.Values)
            {
                pipeline.Stop();
            }

            _pipelines.Clear();
            _connections.Clear();
            _forgottenSessions.Clear();
        }
    }

    /// <summary>
    /// Gives <paramref name="connectionId"/> the publisher claim unless a newer connection took
    /// it from this one and still holds it. Caller holds the lock.
    /// </summary>
    private bool TryHoldClaim(SessionPipeline pipeline, string connectionId, ConnectionState connection)
    {
        string? holder = pipeline.PublisherConnectionId;
        if (string.Equals(holder, connectionId, StringComparison.Ordinal))
        {
            return true;
        }

        if (holder is not null && connection.Displaced)
        {
            return false;
        }

        if (holder is not null && _connections.TryGetValue(holder, out ConnectionState? previous))
        {
            previous.Displaced = true;
        }

        connection.Displaced = false;
        pipeline.PublisherConnectionId = connectionId;
        pipeline.OfflinePending = false;
        pipeline.PublisherOffline = false;
        pipeline.CancelGraceTimer();
        return true;
    }

    private ConnectionState GetOrCreateConnection(string connectionId, string sessionId, DateTimeOffset now)
    {
        if (_connections.TryGetValue(connectionId, out ConnectionState? existing)
            && string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal))
        {
            return existing;
        }

        ConnectionState created = new(sessionId, now);
        _connections[connectionId] = created;
        return created;
    }

    private SessionPipeline GetOrCreatePipeline(string sessionId)
    {
        if (_pipelines.TryGetValue(sessionId, out SessionPipeline? existing))
        {
            return existing;
        }

        SessionPipeline pipeline = new(sessionId);
        _pipelines[sessionId] = pipeline;
        pipeline.Worker = Task.Run(() => RunWorkerAsync(pipeline));
        return pipeline;
    }

    private void StartGraceTimer(SessionPipeline pipeline)
    {
        pipeline.CancelGraceTimer();
        TimeSpan grace = TimeSpan.FromSeconds(_options.Value.PublisherGraceSeconds);
        pipeline.GraceTimer = _timeProvider.CreateTimer(
            _ => OnGraceElapsed(pipeline),
            null,
            grace,
            Timeout.InfiniteTimeSpan);
    }

    private void OnGraceElapsed(SessionPipeline pipeline)
    {
        lock (_lock)
        {
            if (pipeline.PublisherConnectionId is not null
                || !_pipelines.TryGetValue(pipeline.SessionId, out SessionPipeline? current)
                || !ReferenceEquals(current, pipeline))
            {
                return;
            }

            pipeline.CancelGraceTimer();
            pipeline.OfflinePending = true;
            pipeline.PublisherOffline = true;
            pipeline.Signal();
        }
    }

    private async Task RunWorkerAsync(SessionPipeline pipeline)
    {
        CancellationToken stopping = pipeline.Stopping.Token;
        try
        {
            await foreach (bool _ in pipeline.Wakeups.Reader.ReadAllAsync(stopping))
            {
                while (TryTakeWork(pipeline, out WorkItem? item))
                {
                    try
                    {
                        await ProcessAsync(pipeline, item, stopping);
                    }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to apply published playback for Session {SessionRef}",
                            SessionLogRef.For(pipeline.SessionId));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        finally
        {
            // The caller that ended the Session already cleared this state, but an apply that was
            // past its last cancellation check may have written it again since.
            if (pipeline.Ended)
            {
                _applier.Forget(pipeline.SessionId);
                _registry.RemoveUserState(pipeline.SessionId);
            }
        }
    }

    /// <summary>
    /// Takes the pending update before the offline mark, so the grace period ending while an
    /// apply runs never discards the publisher's last update.
    /// </summary>
    private bool TryTakeWork(SessionPipeline pipeline, [NotNullWhen(true)] out WorkItem? item)
    {
        lock (_lock)
        {
            if (pipeline.PendingApply is { } apply)
            {
                pipeline.PendingApply = null;
                item = apply;
                return true;
            }

            if (pipeline.OfflinePending)
            {
                pipeline.OfflinePending = false;
                if (pipeline.PublisherConnectionId is null)
                {
                    item = new PublisherOfflineItem(pipeline.DisplayName);
                    return true;
                }
            }

            if (pipeline.ResolvedCoverArt is { } resolved)
            {
                pipeline.ResolvedCoverArt = null;
                item = new CoverArtItem(resolved.TrackId, resolved.ArtUrl);
                return true;
            }

            if (pipeline.LyricRetryPending)
            {
                pipeline.LyricRetryPending = false;
                item = new LyricRetryItem();
                return true;
            }

            item = null;
            return false;
        }
    }

    private async Task ProcessAsync(SessionPipeline pipeline, WorkItem item, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;

        switch (item)
        {
            case ApplyItem apply:
                bool allowLyrics = TrySpendLyricBudget(pipeline, apply.State);
                (PlaybackState state, TrackInfo? trackToLookUp) = await FillCoverArtAsync(
                    pipeline,
                    apply.State,
                    services,
                    cancellationToken);
                await _applier.ApplyAsync(
                    new PlaybackUpdate(
                        pipeline.SessionId,
                        apply.DisplayName,
                        state,
                        allowLyrics,
                        apply.ClaimedSpotifyTrackId),
                    services.GetRequiredService<ILyricsProvider>(),
                    services.GetRequiredService<ILyricsCacheRepository>(),
                    cancellationToken);
                if (trackToLookUp is not null)
                {
                    _coverArtBackfill.Request(trackToLookUp);
                }

                await TouchIfDueAsync(pipeline, services, cancellationToken);
                break;

            case CoverArtItem coverArt:
                if (string.Equals(pipeline.CoverArtTrackId, coverArt.TrackId, StringComparison.Ordinal))
                {
                    pipeline.CoverArtUrl = coverArt.ArtUrl;
                    await _applier.ApplyCoverArtAsync(
                        pipeline.SessionId,
                        coverArt.TrackId,
                        coverArt.ArtUrl,
                        cancellationToken);
                }

                break;

            case PublisherOfflineItem offline:
                await _applier.MarkPublisherOfflineAsync(pipeline.SessionId, offline.DisplayName, cancellationToken);
                break;

            case LyricRetryItem:
                await RetryDeniedLyricsAsync(pipeline, services, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Gives a track the player sent without a cover the one the catalogue has for it. The cache
    /// is read once per track; later updates for the same track reuse the answer, because the
    /// player keeps sending them without a cover. A track the cache does not know is returned
    /// for a background lookup, to request once its state is broadcast. Runs on the Session's
    /// worker only.
    /// </summary>
    private async Task<(PlaybackState State, TrackInfo? TrackToLookUp)> FillCoverArtAsync(
        SessionPipeline pipeline,
        PlaybackState state,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (state.CurrentTrack is null)
        {
            // Playing the same track again after a stop asks once more, in case the catalogue
            // could not be reached the first time.
            pipeline.CoverArtTrackId = null;
            pipeline.CoverArtUrl = null;
            lock (_lock)
            {
                pipeline.AwaitedCoverArtTrackId = null;
                pipeline.ResolvedCoverArt = null;
            }
        }

        if (state.CurrentTrack is not { AlbumArtUrl: null } track || !_coverArtBackfill.IsEnabled)
        {
            return (state, null);
        }

        TrackInfo? trackToLookUp = null;
        if (!string.Equals(pipeline.CoverArtTrackId, track.Id, StringComparison.Ordinal))
        {
            CoverArtLookup cached = await services.GetRequiredService<ICoverArtService>()
                .GetCachedAsync(track, cancellationToken);
            pipeline.CoverArtTrackId = track.Id;
            pipeline.CoverArtUrl = cached.ArtUrl;

            bool lookUp = cached.Status == CoverArtLookupStatus.Unresolved;
            lock (_lock)
            {
                pipeline.AwaitedCoverArtTrackId = lookUp ? track.Id : null;
                pipeline.ResolvedCoverArt = null;
            }

            trackToLookUp = lookUp ? track : null;
        }

        return pipeline.CoverArtUrl is { } artUrl
            ? (state with { CurrentTrack = track with { AlbumArtUrl = artUrl } }, trackToLookUp)
            : (state, trackToLookUp);
    }

    /// <summary>Hands a found cover to the worker of every Session waiting for that track.</summary>
    private void OnCoverArtResolved(object? sender, CoverArtResolved resolved)
    {
        lock (_lock)
        {
            foreach (SessionPipeline pipeline in _pipelines.Values)
            {
                if (string.Equals(pipeline.AwaitedCoverArtTrackId, resolved.TrackId, StringComparison.Ordinal))
                {
                    pipeline.AwaitedCoverArtTrackId = null;
                    pipeline.ResolvedCoverArt = resolved;
                    pipeline.Signal();
                }
            }
        }
    }

    /// <summary>
    /// Counts track changes, not updates: a Session may start at most
    /// <see cref="LYRIC_FETCHES_PER_MINUTE"/> lyric lookups per rolling minute. A track refused
    /// lyrics gets them once the budget frees up, because the client publishes only on changes
    /// and may send nothing more while that track plays. Runs on the Session's worker only.
    /// </summary>
    private bool TrySpendLyricBudget(SessionPipeline pipeline, PlaybackState state)
    {
        string? trackId = state.CurrentTrack?.Id;
        bool trackChanged = trackId is not null
            && !string.Equals(trackId, pipeline.LastTrackId, StringComparison.Ordinal);
        pipeline.LastTrackId = trackId;
        if (!string.Equals(trackId, pipeline.LyricsDeniedTrackId, StringComparison.Ordinal))
        {
            CancelLyricRetry(pipeline);
        }

        if (!trackChanged)
        {
            return true;
        }

        if (!TryTakeLyricFetch(pipeline))
        {
            _logger.LogWarning(
                "Session {SessionRef} is over its lyric budget; sending the new track without lyrics for now",
                SessionLogRef.For(pipeline.SessionId));
            pipeline.LyricsDeniedTrackId = trackId;
            ScheduleLyricRetry(pipeline);
            return false;
        }

        return true;
    }

    private async Task RetryDeniedLyricsAsync(
        SessionPipeline pipeline,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (pipeline.LyricsDeniedTrackId is not { } trackId)
        {
            return;
        }

        if (!TryTakeLyricFetch(pipeline))
        {
            ScheduleLyricRetry(pipeline);
            return;
        }

        pipeline.LyricsDeniedTrackId = null;
        await _applier.RefetchLyricsAsync(
            pipeline.SessionId,
            trackId,
            services.GetRequiredService<ILyricsProvider>(),
            services.GetRequiredService<ILyricsCacheRepository>(),
            cancellationToken);
    }

    private bool TryTakeLyricFetch(SessionPipeline pipeline)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        while (pipeline.LyricFetches.Count > 0 && pipeline.LyricFetches.Peek() + LyricBudgetWindow <= now)
        {
            pipeline.LyricFetches.Dequeue();
        }

        if (pipeline.LyricFetches.Count >= LYRIC_FETCHES_PER_MINUTE)
        {
            return false;
        }

        pipeline.LyricFetches.Enqueue(now);
        return true;
    }

    /// <summary>Wakes the worker when the oldest lyric fetch leaves the budget window.</summary>
    private void ScheduleLyricRetry(SessionPipeline pipeline)
    {
        TimeSpan due = pipeline.LyricFetches.Peek() + LyricBudgetWindow - _timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (pipeline.Stopping.IsCancellationRequested)
            {
                return;
            }

            pipeline.CancelLyricRetryTimer();
            pipeline.LyricRetryTimer = _timeProvider.CreateTimer(
                _ => OnLyricRetryDue(pipeline),
                null,
                due > TimeSpan.Zero ? due : TimeSpan.Zero,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void OnLyricRetryDue(SessionPipeline pipeline)
    {
        lock (_lock)
        {
            if (pipeline.Stopping.IsCancellationRequested)
            {
                return;
            }

            pipeline.LyricRetryPending = true;
            pipeline.Signal();
        }
    }

    private void CancelLyricRetry(SessionPipeline pipeline)
    {
        pipeline.LyricsDeniedTrackId = null;
        lock (_lock)
        {
            pipeline.CancelLyricRetryTimer();
            pipeline.LyricRetryPending = false;
        }
    }

    private async Task TouchIfDueAsync(
        SessionPipeline pipeline,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (now - pipeline.LastTouchUtc < TouchInterval)
        {
            return;
        }

        pipeline.LastTouchUtc = now;
        try
        {
            await services.GetRequiredService<ILocalSessionService>().TouchAsync(pipeline.SessionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to record activity for Local Session {SessionRef}",
                SessionLogRef.For(pipeline.SessionId));
        }
    }

    /// <summary>
    /// Turns a publish into a Local playback state, or rejects it. Text fields are cleaned first;
    /// over-long or missing required fields reject; a bad art URL or Spotify id is dropped;
    /// position and rate are clamped.
    /// </summary>
    private static bool TryBuildState(
        [NotNullWhen(true)] LocalPlaybackUpdateDto? update,
        DateTimeOffset receivedAt,
        [NotNullWhen(true)] out PlaybackState? state,
        out string? claimedSpotifyTrackId)
    {
        state = null;
        claimedSpotifyTrackId = null;

        if (update?.Player is not { } player)
        {
            return false;
        }

        string? playerId = CleanText(player.Id);
        string? deviceName = CleanText(update.DeviceName);
        if (string.IsNullOrEmpty(playerId)
            || playerId.Length > MAX_PLAYER_FIELD_LENGTH
            || (CleanText(player.Name)?.Length ?? 0) > MAX_PLAYER_FIELD_LENGTH
            || (deviceName?.Length ?? 0) > MAX_PLAYER_FIELD_LENGTH)
        {
            return false;
        }

        PlaybackState baseState = new()
        {
            IsPlaying = false,
            TimestampUtc = ResolveTimestamp(update.PositionAtServerMs, receivedAt),
            DeviceName = deviceName,
            Source = PlaybackSource.Local,
            Capabilities = update.Capabilities.ToModel(),
            PlaybackRate = ClampRate(update.Rate)
        };

        if (update.Track is null)
        {
            state = baseState;
            return true;
        }

        LocalTrackDto track = update.Track;
        string? title = CleanText(track.Title);
        string? artist = CleanText(track.Artist);
        string? album = CleanText(track.Album);
        if (string.IsNullOrEmpty(title)
            || string.IsNullOrEmpty(artist)
            || title.Length > MAX_TRACK_FIELD_LENGTH
            || artist.Length > MAX_TRACK_FIELD_LENGTH
            || (album?.Length ?? 0) > MAX_TRACK_FIELD_LENGTH
            || track.DurationMs < MIN_DURATION_MS
            || track.DurationMs > MAX_DURATION_MS)
        {
            return false;
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(track.DurationMs);
        claimedSpotifyTrackId = TrackIdentity.TryParseSpotifyTrackId(track.SpotifyTrackId, out string? spotifyId)
            ? spotifyId
            : null;

        state = baseState with
        {
            CurrentTrack = new TrackInfo
            {
                Id = TrackIdentity.DeriveLocalKey(title, artist, duration),
                Title = title,
                Artist = artist,
                Album = string.IsNullOrEmpty(album) ? null : album,
                AlbumArtUrl = SanitizeArtUrl(track.AlbumArtUrl),
                Duration = duration
            },
            IsPlaying = update.IsPlaying,
            Progress = TimeSpan.FromMilliseconds(Math.Clamp(update.PositionMs, 0, track.DurationMs))
        };
        return true;
    }

    /// <summary>
    /// Replaces control characters and the Unicode line and paragraph separators with a space,
    /// so a published name cannot start a forged line in a log, then collapses runs of spaces
    /// and trims.
    /// </summary>
    private static string? CleanText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        StringBuilder cleaned = new(value.Length);
        foreach (char c in value)
        {
            char mapped = char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c;
            if (mapped == ' ' && cleaned.Length > 0 && cleaned[^1] == ' ')
            {
                continue;
            }

            cleaned.Append(mapped);
        }

        return cleaned.ToString().Trim();
    }

    /// <summary>
    /// Keeps the client's server-clock sample time when it is plausible, so uplink jitter stays
    /// out of every viewer's timeline; otherwise uses the receipt time.
    /// </summary>
    private static DateTimeOffset ResolveTimestamp(long? positionAtServerMs, DateTimeOffset receivedAt)
    {
        if (positionAtServerMs is not { } sampledMs)
        {
            return receivedAt;
        }

        long earliestMs = (receivedAt - TimestampPastTolerance).ToUnixTimeMilliseconds();
        long latestMs = (receivedAt + TimestampFutureTolerance).ToUnixTimeMilliseconds();
        return sampledMs >= earliestMs && sampledMs <= latestMs
            ? DateTimeOffset.FromUnixTimeMilliseconds(sampledMs)
            : receivedAt;
    }

    private static double ClampRate(double rate) =>
        double.IsNaN(rate) ? 1.0 : Math.Clamp(rate, MIN_RATE, MAX_RATE);

    private static string? SanitizeArtUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || url.Length > MAX_ALBUM_ART_URL_LENGTH
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return url;
    }

    private abstract record WorkItem;

    private sealed record ApplyItem(PlaybackState State, string DisplayName, string? ClaimedSpotifyTrackId) : WorkItem;

    private sealed record PublisherOfflineItem(string DisplayName) : WorkItem;

    private sealed record LyricRetryItem : WorkItem;

    private sealed record CoverArtItem(string TrackId, string ArtUrl) : WorkItem;

    private sealed class ConnectionState
    {
        private double _tokens = TOKEN_BUCKET_BURST;
        private DateTimeOffset _lastRefillUtc;

        public ConnectionState(string sessionId, DateTimeOffset now)
        {
            SessionId = sessionId;
            _lastRefillUtc = now;
        }

        public string SessionId { get; }

        public long LastSequence { get; set; }

        /// <summary>True once a newer connection of the Session took the publisher claim from this one.</summary>
        public bool Displaced { get; set; }

        public bool TryTakeToken(DateTimeOffset now)
        {
            if (now > _lastRefillUtc)
            {
                _tokens = Math.Min(
                    TOKEN_BUCKET_BURST,
                    _tokens + ((now - _lastRefillUtc).TotalSeconds * TOKEN_REFILL_PER_SECOND));
                _lastRefillUtc = now;
            }

            if (_tokens < 1)
            {
                return false;
            }

            _tokens--;
            return true;
        }
    }

    private sealed class SessionPipeline
    {
        public SessionPipeline(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public string DisplayName { get; set; } = string.Empty;

        public string? PublisherConnectionId { get; set; }

        public ITimer? GraceTimer { get; set; }

        /// <summary>Fires when a track refused lyrics may have them. Guarded by the ingest lock.</summary>
        public ITimer? LyricRetryTimer { get; set; }

        /// <summary>Set when <see cref="LyricRetryTimer"/> fires. Guarded by the ingest lock.</summary>
        public bool LyricRetryPending { get; set; }

        /// <summary>The current track, when it was broadcast without lyrics over the budget. Worker only.</summary>
        public string? LyricsDeniedTrackId { get; set; }

        /// <summary>The newest update not yet applied; a newer one replaces it. Guarded by the ingest lock.</summary>
        public ApplyItem? PendingApply { get; set; }

        /// <summary>Set when the grace period ends with no publisher. Guarded by the ingest lock.</summary>
        public bool OfflinePending { get; set; }

        /// <summary>Like <see cref="OfflinePending"/>, but stays set until a connection publishes again.</summary>
        public bool PublisherOffline { get; set; }

        /// <summary>Wakes the worker; coalesced, since the worker drains both slots on each wakeup.</summary>
        public Channel<bool> Wakeups { get; } = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        public CancellationTokenSource Stopping { get; } = new();

        public Task Worker { get; set; } = Task.CompletedTask;

        /// <summary>Set when the Session ended, so the worker clears its state once it stops.</summary>
        public bool Ended { get; set; }

        public string? LastTrackId { get; set; }

        public Queue<DateTimeOffset> LyricFetches { get; } = new();

        /// <summary>The track <see cref="CoverArtUrl"/> was read for. Worker only.</summary>
        public string? CoverArtTrackId { get; set; }

        /// <summary>The catalogue's cover for <see cref="CoverArtTrackId"/>, or null when none is known. Worker only.</summary>
        public string? CoverArtUrl { get; set; }

        /// <summary>The track a background lookup was requested for. Guarded by the ingest lock.</summary>
        public string? AwaitedCoverArtTrackId { get; set; }

        /// <summary>A cover the background lookup found, for the worker to apply. Guarded by the ingest lock.</summary>
        public CoverArtResolved? ResolvedCoverArt { get; set; }

        public DateTimeOffset LastTouchUtc { get; set; } = DateTimeOffset.MinValue;

        public void Signal() => Wakeups.Writer.TryWrite(true);

        public void CancelGraceTimer()
        {
            GraceTimer?.Dispose();
            GraceTimer = null;
        }

        public void CancelLyricRetryTimer()
        {
            LyricRetryTimer?.Dispose();
            LyricRetryTimer = null;
        }

        public void Stop()
        {
            CancelGraceTimer();
            CancelLyricRetryTimer();
            Stopping.Cancel();
            Wakeups.Writer.TryComplete();
        }
    }
}
