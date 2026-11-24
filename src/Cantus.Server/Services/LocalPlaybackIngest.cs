using System.Diagnostics.CodeAnalysis;
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

    /// <summary>Drops everything held for a Session that ended.</summary>
    void Forget(string sessionId);
}

/// <summary>
/// Takes playback that Local Sessions publish and applies it in the background, one Session at
/// a time. Each Session has one publisher connection, a queue that keeps only the newest update,
/// and a lyric budget; each connection has a token bucket and a sequence check.
/// </summary>
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

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybackUpdateApplier _applier;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<LocalSessionOptions> _options;
    private readonly ILogger<LocalPlaybackIngest> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, ConnectionState> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionPipeline> _pipelines = new(StringComparer.Ordinal);
    private bool _disposed;

    public LocalPlaybackIngest(
        IServiceScopeFactory scopeFactory,
        IPlaybackUpdateApplier applier,
        TimeProvider timeProvider,
        IOptions<LocalSessionOptions> options,
        ILogger<LocalPlaybackIngest> logger)
    {
        _scopeFactory = scopeFactory;
        _applier = applier;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public LocalPublishResult Submit(string connectionId, UserSession session, LocalPlaybackUpdateDto? update)
    {
        if (session.Kind != SessionKind.Local)
        {
            return LocalPublishResult.NotLocalSession;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

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
            if (pipeline.PublisherConnectionId is null)
            {
                pipeline.PublisherConnectionId = connectionId;
                pipeline.CancelGraceTimer();
            }
            else if (!string.Equals(pipeline.PublisherConnectionId, connectionId, StringComparison.Ordinal))
            {
                return LocalPublishResult.NotPublisher;
            }

            if (update.Sequence <= connection.LastSequence)
            {
                return LocalPublishResult.Accepted;
            }

            connection.LastSequence = update.Sequence;
            pipeline.DisplayName = session.DisplayName;
            pipeline.Queue.Writer.TryWrite(new ApplyItem(state, session.DisplayName, claimedSpotifyTrackId));
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

    public void Forget(string sessionId)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<string, ConnectionState> entry in _connections.ToList())
            {
                if (string.Equals(entry.Value.SessionId, sessionId, StringComparison.Ordinal))
                {
                    _connections.Remove(entry.Key);
                }
            }

            if (_pipelines.Remove(sessionId, out SessionPipeline? pipeline))
            {
                pipeline.Stop();
            }
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
            foreach (SessionPipeline pipeline in _pipelines.Values)
            {
                pipeline.Stop();
            }

            _pipelines.Clear();
            _connections.Clear();
        }
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
        _ = Task.Run(() => RunWorkerAsync(pipeline));
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
            pipeline.Queue.Writer.TryWrite(new PublisherOfflineItem(pipeline.DisplayName));
        }
    }

    private async Task RunWorkerAsync(SessionPipeline pipeline)
    {
        CancellationToken stopping = pipeline.Stopping.Token;
        try
        {
            await foreach (WorkItem item in pipeline.Queue.Reader.ReadAllAsync(stopping))
            {
                try
                {
                    await ProcessAsync(pipeline, item, stopping);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    break;
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
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
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
                await _applier.ApplyAsync(
                    new PlaybackUpdate(
                        pipeline.SessionId,
                        apply.DisplayName,
                        apply.State,
                        allowLyrics,
                        apply.ClaimedSpotifyTrackId),
                    services.GetRequiredService<ILyricsProvider>(),
                    services.GetRequiredService<ILyricsCacheRepository>(),
                    cancellationToken);
                await TouchIfDueAsync(pipeline, services, cancellationToken);
                break;

            case PublisherOfflineItem offline:
                await _applier.MarkPublisherOfflineAsync(pipeline.SessionId, offline.DisplayName, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Counts track changes, not updates: a Session may start at most
    /// <see cref="LYRIC_FETCHES_PER_MINUTE"/> lyric lookups per rolling minute. Runs on the
    /// Session's worker only, so it needs no lock.
    /// </summary>
    private bool TrySpendLyricBudget(SessionPipeline pipeline, PlaybackState state)
    {
        string? trackId = state.CurrentTrack?.Id;
        bool trackChanged = trackId is not null
            && !string.Equals(trackId, pipeline.LastTrackId, StringComparison.Ordinal);
        pipeline.LastTrackId = trackId;
        if (!trackChanged)
        {
            return true;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        while (pipeline.LyricFetches.Count > 0 && pipeline.LyricFetches.Peek() + LyricBudgetWindow <= now)
        {
            pipeline.LyricFetches.Dequeue();
        }

        if (pipeline.LyricFetches.Count >= LYRIC_FETCHES_PER_MINUTE)
        {
            _logger.LogWarning(
                "Session {SessionRef} is over its lyric budget; sending the new track without lyrics",
                SessionLogRef.For(pipeline.SessionId));
            return false;
        }

        pipeline.LyricFetches.Enqueue(now);
        return true;
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
    /// Turns a publish into a Local playback state, or rejects it. Over-long or missing required
    /// fields reject; a bad art URL or Spotify id is dropped; position and rate are clamped.
    /// </summary>
    private static bool TryBuildState(
        [NotNullWhen(true)] LocalPlaybackUpdateDto? update,
        DateTimeOffset receivedAt,
        [NotNullWhen(true)] out PlaybackState? state,
        out string? claimedSpotifyTrackId)
    {
        state = null;
        claimedSpotifyTrackId = null;

        if (update?.Player is not { Id: { } playerId } player
            || string.IsNullOrWhiteSpace(playerId)
            || playerId.Length > MAX_PLAYER_FIELD_LENGTH
            || (player.Name?.Length ?? 0) > MAX_PLAYER_FIELD_LENGTH
            || (update.DeviceName?.Length ?? 0) > MAX_PLAYER_FIELD_LENGTH)
        {
            return false;
        }

        PlaybackState baseState = new()
        {
            IsPlaying = false,
            TimestampUtc = ResolveTimestamp(update.PositionAtServerMs, receivedAt),
            DeviceName = update.DeviceName,
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
        if (string.IsNullOrWhiteSpace(track.Title)
            || string.IsNullOrWhiteSpace(track.Artist)
            || track.Title.Length > MAX_TRACK_FIELD_LENGTH
            || track.Artist.Length > MAX_TRACK_FIELD_LENGTH
            || (track.Album?.Length ?? 0) > MAX_TRACK_FIELD_LENGTH
            || track.DurationMs < MIN_DURATION_MS
            || track.DurationMs > MAX_DURATION_MS)
        {
            return false;
        }

        string title = track.Title.Trim();
        string artist = track.Artist.Trim();
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
                Album = string.IsNullOrWhiteSpace(track.Album) ? null : track.Album.Trim(),
                AlbumArtUrl = SanitizeArtUrl(track.AlbumArtUrl),
                Duration = duration
            },
            IsPlaying = update.IsPlaying,
            Progress = TimeSpan.FromMilliseconds(Math.Clamp(update.PositionMs, 0, track.DurationMs))
        };
        return true;
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

        public Channel<WorkItem> Queue { get; } = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        public CancellationTokenSource Stopping { get; } = new();

        public string? LastTrackId { get; set; }

        public Queue<DateTimeOffset> LyricFetches { get; } = new();

        public DateTimeOffset LastTouchUtc { get; set; } = DateTimeOffset.MinValue;

        public void CancelGraceTimer()
        {
            GraceTimer?.Dispose();
            GraceTimer = null;
        }

        public void Stop()
        {
            CancelGraceTimer();
            Stopping.Cancel();
            Queue.Writer.TryComplete();
        }
    }
}
