using System;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Publishes the followed local player to the server. It sends a full snapshot after every connect,
/// then an update when the followed player, track, play status, capabilities or rate change, on a seek,
/// and when the position drifts more than <see cref="DRIFT_THRESHOLD_MS"/> from what was last sent.
/// Only the latest state is sent: changes that arrive while a publish is in flight collapse into one.
/// Has no UI dependency; all events may arrive on any thread.
/// </summary>
public sealed class LocalPlaybackPublisher : IDisposable
{
    public const long DRIFT_THRESHOLD_MS = 400;
    public const int MAX_TRACK_TEXT_LENGTH = 300;
    public const int MAX_PLAYER_TEXT_LENGTH = 128;
    public const int MAX_ART_URL_LENGTH = 2048;
    public const string NO_PLAYER_ID = "none";
    public const string NO_PLAYER_NAME = "None";

    public static readonly TimeSpan TICK_INTERVAL = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan RATE_LIMIT_FIRST_BACKOFF = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RATE_LIMIT_MAX_BACKOFF = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan NOT_PUBLISHER_FIRST_BACKOFF = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan NOT_PUBLISHER_MAX_BACKOFF = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan SERVER_ERROR_FIRST_BACKOFF = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SERVER_ERROR_MAX_BACKOFF = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SEND_FAILURE_RETRY = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Viewers project at 1x, so at any other rate they drift from the player by |rate - 1| per second.
    /// The drift threshold grows with that so corrections go out at most about this often.
    /// </summary>
    public static readonly TimeSpan MIN_RATE_CORRECTION_INTERVAL = TimeSpan.FromSeconds(1);

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<LocalPlaybackPublisher>();

    private readonly ILocalPlayerSource _source;
    private readonly ILocalPlaybackSink _sink;
    private readonly PlayerSelectionPolicy _policy;
    private readonly LocalPlayerPreferences _preferences;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    private ITimer? _timer;
    private bool _running;
    private bool _stoppedByServer;
    private bool _sending;
    private bool _dirty;
    private bool _disposed;
    private long _sequence;
    private string? _sequenceConnectionId;
    private TimeSpan _blockedUntil;
    private TimeSpan _rateLimitBackoff = RATE_LIMIT_FIRST_BACKOFF;
    private TimeSpan _notPublisherBackoff = NOT_PUBLISHER_FIRST_BACKOFF;
    private TimeSpan _serverErrorBackoff = SERVER_ERROR_FIRST_BACKOFF;
    private PublishedState? _lastSent;
    private LocalPlayerSnapshot? _followed;
    private string? _lastPlayerId;
    private string? _lastPlayerName;
    private LocalPlayerSnapshot? _reportedFollowed;

    public LocalPlaybackPublisher(
        ILocalPlayerSource source,
        ILocalPlaybackSink sink,
        PlayerSelectionPolicy policy,
        LocalPlayerPreferences preferences,
        TimeProvider clock)
    {
        _source = source;
        _sink = sink;
        _policy = policy;
        _preferences = preferences;
        _clock = clock;

        _sink.Connected += OnConnected;
    }

    /// <summary>Raised after the followed player changes, including to none when publishing stops.</summary>
    public event Action? FollowedPlayerChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>True after the server answered NotLocalSession; cleared by the next <see cref="Start"/>.</summary>
    public bool IsStoppedByServer
    {
        get
        {
            lock (_gate)
            {
                return _stoppedByServer;
            }
        }
    }

    public LocalPlayerSnapshot? FollowedPlayer
    {
        get
        {
            lock (_gate)
            {
                return _followed;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || (_running && !_stoppedByServer))
            {
                return;
            }

            if (!_running)
            {
                _source.PlayersChanged += OnPlayersChanged;
                _timer = _clock.CreateTimer(_ => Pump(), null, TICK_INTERVAL, TICK_INTERVAL);
            }

            _running = true;
            _stoppedByServer = false;
            ForceSnapshot();
        }

        _logger.LogInformation("Local playback publisher started");
        Pump();
    }

    public void Stop()
    {
        ITimer? timer;
        bool followedChanged;
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _source.PlayersChanged -= OnPlayersChanged;
            timer = _timer;
            _timer = null;
            _followed = null;
            followedChanged = TakeFollowedChange();
        }

        timer?.Dispose();
        _logger.LogInformation("Local playback publisher stopped");
        if (followedChanged)
        {
            FollowedPlayerChanged?.Invoke();
        }
    }

    /// <summary>Chooses the followed player again now, for example after a player was switched off.</summary>
    public void Refresh() => Pump();

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
        }

        _sink.Connected -= OnConnected;
    }

    private void OnPlayersChanged() => Pump();

    private void OnConnected()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            ForceSnapshot();
        }

        Pump();
    }

    private void ForceSnapshot()
    {
        _lastSent = null;
        _dirty = true;
        _blockedUntil = TimeSpan.Zero;
        ResetBackoffs();
    }

    private void ResetBackoffs()
    {
        _rateLimitBackoff = RATE_LIMIT_FIRST_BACKOFF;
        _notPublisherBackoff = NOT_PUBLISHER_FIRST_BACKOFF;
        _serverErrorBackoff = SERVER_ERROR_FIRST_BACKOFF;
    }

    private void Pump()
    {
        bool followedChanged;
        bool send = false;
        lock (_gate)
        {
            if (!_running || _stoppedByServer)
            {
                return;
            }

            TimeSpan now = MonotonicClock.Now(_clock);
            Evaluate(now);
            followedChanged = TakeFollowedChange();
            if (!_sending && CanSendNow(now))
            {
                _sending = true;
                send = true;
            }
        }

        if (followedChanged)
        {
            FollowedPlayerChanged?.Invoke();
        }

        if (send)
        {
            _ = SendLoopAsync();
        }
    }

    private bool TakeFollowedChange()
    {
        bool changed = !string.Equals(_followed?.InstanceId, _reportedFollowed?.InstanceId, StringComparison.Ordinal)
            || !string.Equals(_followed?.AppName, _reportedFollowed?.AppName, StringComparison.Ordinal);
        _reportedFollowed = _followed;
        return changed;
    }

    private bool CanSendNow(TimeSpan now) =>
        _running && !_stoppedByServer && _dirty && now >= _blockedUntil && _sink.IsConnected;

    private async Task SendLoopAsync()
    {
        while (true)
        {
            LocalPlaybackUpdatePayload payload;
            string? sentOn;
            lock (_gate)
            {
                TimeSpan now = MonotonicClock.Now(_clock);
                if (!CanSendNow(now))
                {
                    _sending = false;
                    return;
                }

                Evaluate(now);
                payload = BuildPayload(now);
                sentOn = _sequenceConnectionId;
            }

            int result;
            try
            {
                result = await _sink.PublishAsync(payload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Publishing local playback failed");
                result = (int)LocalPublishResult.NotConnected;
            }

            lock (_gate)
            {
                // A result from an earlier connection says nothing about the current one.
                if (string.Equals(sentOn, _sink.ConnectionId, StringComparison.Ordinal))
                {
                    HandleResult(result, payload);
                }
            }
        }
    }

    private void Evaluate(TimeSpan now)
    {
        _followed = _policy.Select(_source.Players, _preferences.IsEnabled, now);
        if (_followed is not null)
        {
            _lastPlayerId = _followed.AppId;
            _lastPlayerName = _followed.AppName;
        }

        PublishedState desired = Describe(_followed, now);
        if (_lastSent is null || HasMeaningfulChange(_lastSent, desired, now))
        {
            _dirty = true;
        }
    }

    private PublishedState Describe(LocalPlayerSnapshot? player, TimeSpan now)
    {
        LocalTrack? track = player is not null
            && player.Status != LocalPlayerStatus.Stopped
            && PublishWorthiness.IsWorthPublishing(player.Track)
                ? player.Track
                : null;

        PublishedKey key = new(
            player?.InstanceId,
            player?.AppId ?? _lastPlayerId ?? NO_PLAYER_ID,
            player?.AppName ?? _lastPlayerName ?? NO_PLAYER_NAME,
            track,
            track is not null && player!.Status == LocalPlayerStatus.Playing,
            player is not null && double.IsFinite(player.Rate) ? player.Rate : 1.0,
            player?.Capabilities ?? LocalPlayerCapabilities.None,
            player?.SeekGeneration ?? 0);

        long positionMs = track is null ? 0 : (long)player!.ProjectPosition(now).TotalMilliseconds;
        return new PublishedState(key, positionMs, now);
    }

    private static bool HasMeaningfulChange(PublishedState last, PublishedState desired, TimeSpan now)
    {
        if (last.Key != desired.Key)
        {
            return true;
        }

        if (desired.Key.Track is null)
        {
            return false;
        }

        // Compared with what viewers show, which is the last sent position projected at 1x.
        double expectedMs = last.PositionMs;
        double thresholdMs = DRIFT_THRESHOLD_MS;
        if (last.Key.IsPlaying)
        {
            expectedMs += (now - last.SampledAt).TotalMilliseconds;
            thresholdMs = Math.Max(
                thresholdMs,
                Math.Abs(last.Key.Rate - 1.0) * MIN_RATE_CORRECTION_INTERVAL.TotalMilliseconds);
        }

        double durationMs = desired.Key.Track.Duration.TotalMilliseconds;
        expectedMs = Math.Clamp(expectedMs, 0, durationMs);
        return Math.Abs(desired.PositionMs - expectedMs) > thresholdMs;
    }

    private LocalPlaybackUpdatePayload BuildPayload(TimeSpan now)
    {
        // The server orders updates per connection, so the sequence follows the connection the payload
        // goes out on. The Connected event can arrive after the first send on a new connection.
        string? connectionId = _sink.ConnectionId;
        if (!string.Equals(connectionId, _sequenceConnectionId, StringComparison.Ordinal))
        {
            _sequenceConnectionId = connectionId;
            _sequence = 0;
        }

        PublishedState state = Describe(_followed, now);
        long? offsetMs = _sink.ServerClockOffsetMs;
        long? positionAtServerMs = offsetMs is long offset
            ? _clock.GetUtcNow().ToUnixTimeMilliseconds() + offset
            : null;

        _lastSent = state;
        _dirty = false;

        PublishedKey key = state.Key;
        string playerName = Truncate(key.PlayerName, MAX_PLAYER_TEXT_LENGTH);
        return new LocalPlaybackUpdatePayload
        {
            Sequence = ++_sequence,
            Player = new LocalPlayerInfoPayload
            {
                Id = Truncate(key.PlayerId, MAX_PLAYER_TEXT_LENGTH),
                Name = playerName,
            },
            DeviceName = playerName,
            Track = ToPayload(key.Track),
            IsPlaying = key.IsPlaying,
            PositionMs = state.PositionMs,
            Rate = key.Rate,
            PositionAtServerMs = positionAtServerMs,
            Capabilities = new PlayerCapabilitiesPayload
            {
                CanPlay = key.Capabilities.CanPlay,
                CanPause = key.Capabilities.CanPause,
                CanGoNext = key.Capabilities.CanGoNext,
                CanGoPrevious = key.Capabilities.CanGoPrevious,
                CanSeek = key.Capabilities.CanSeek,
            },
        };
    }

    private static LocalTrackPayload? ToPayload(LocalTrack? track)
    {
        if (track is null)
        {
            return null;
        }

        return new LocalTrackPayload
        {
            Title = Truncate(track.Title, MAX_TRACK_TEXT_LENGTH),
            Artist = Truncate(track.Artist, MAX_TRACK_TEXT_LENGTH),
            Album = track.Album is null ? null : Truncate(track.Album, MAX_TRACK_TEXT_LENGTH),
            DurationMs = (long)track.Duration.TotalMilliseconds,
            AlbumArtUrl = track.AlbumArtUrl is { Length: <= MAX_ART_URL_LENGTH } url ? url : null,
            SpotifyTrackId = track.SpotifyTrackId,
        };
    }

    private void HandleResult(int result, LocalPlaybackUpdatePayload payload)
    {
        TimeSpan now = MonotonicClock.Now(_clock);
        switch ((LocalPublishResult)result)
        {
            case LocalPublishResult.Accepted:
                ResetBackoffs();
                _logger.LogDebug("Published local playback {Sequence}", payload.Sequence);
                break;

            case LocalPublishResult.RateLimited:
                _logger.LogWarning("Local playback publish rate limited; retrying in {Delay}", _rateLimitBackoff);
                RetryAt(now + _rateLimitBackoff);
                _rateLimitBackoff = DoubleUpTo(_rateLimitBackoff, RATE_LIMIT_MAX_BACKOFF);
                break;

            case LocalPublishResult.NotPublisher:
                _logger.LogInformation(
                    "Another connection publishes for this Session; retrying in {Delay}",
                    _notPublisherBackoff);
                RetryAt(now + _notPublisherBackoff);
                _notPublisherBackoff = DoubleUpTo(_notPublisherBackoff, NOT_PUBLISHER_MAX_BACKOFF);
                break;

            case LocalPublishResult.ServerError:
                _logger.LogWarning(
                    "The server failed local playback update {Sequence}; retrying in {Delay}",
                    payload.Sequence,
                    _serverErrorBackoff);
                RetryAt(now + _serverErrorBackoff);
                _serverErrorBackoff = DoubleUpTo(_serverErrorBackoff, SERVER_ERROR_MAX_BACKOFF);
                break;

            case LocalPublishResult.NotLocalSession:
                _logger.LogWarning("The server says this is not a Local Session; local publishing stopped");
                _stoppedByServer = true;
                break;

            case LocalPublishResult.Invalid:
                _logger.LogWarning("The server rejected local playback update {Sequence} as invalid", payload.Sequence);
                break;

            default:
                RetryAt(now + SEND_FAILURE_RETRY);
                break;
        }
    }

    private void RetryAt(TimeSpan at)
    {
        _blockedUntil = at;
        _lastSent = null;
        _dirty = true;
    }

    private static TimeSpan DoubleUpTo(TimeSpan backoff, TimeSpan max) =>
        TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, max.Ticks));

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    private sealed record PublishedKey(
        string? InstanceId,
        string PlayerId,
        string PlayerName,
        LocalTrack? Track,
        bool IsPlaying,
        double Rate,
        LocalPlayerCapabilities Capabilities,
        int SeekGeneration);

    private sealed record PublishedState(PublishedKey Key, long PositionMs, TimeSpan SampledAt);
}
