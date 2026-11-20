using System.Collections.Concurrent;
using Cantus.Core.Models;

namespace Cantus.Server.Services;

public sealed class PlaybackSessionRegistry : IPlaybackSessionRegistry
{
    public const int DISPLAY_ACTIVITY_MIN_INTERVAL_MS = 5000;

    private readonly ConcurrentDictionary<string, string?> _connectionSubscriptions = new();
    private readonly ConcurrentDictionary<string, UserPlaybackSnapshot> _userSnapshots = new();
    private readonly ConcurrentDictionary<string, bool> _connectionVisibility = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastDisplayActivityUtc = new();
    private readonly object _connectionLock = new();
    private readonly TimeProvider _timeProvider;

    public event EventHandler? OnClientsConnected;
    public event EventHandler? OnClientsEmpty;
    public event EventHandler? OnSessionsChanged;
    public event EventHandler<string>? OnUserActivityRequested;

    public PlaybackSessionRegistry()
        : this(TimeProvider.System)
    {
    }

    public PlaybackSessionRegistry(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void RequestUserActivity(string userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            OnUserActivityRequested?.Invoke(this, userId);
        }
    }

    public void RequestDisplayActivity(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool allowed = false;
        _lastDisplayActivityUtc.AddOrUpdate(
            userId,
            _ =>
            {
                allowed = true;
                return now;
            },
            (_, last) =>
            {
                allowed = now - last >= TimeSpan.FromMilliseconds(DISPLAY_ACTIVITY_MIN_INTERVAL_MS);
                return allowed ? now : last;
            });

        if (allowed)
        {
            OnUserActivityRequested?.Invoke(this, userId);
        }
    }

    public int ConnectedClientsCount => _connectionSubscriptions.Count;
    public bool HasConnectedClients => !_connectionSubscriptions.IsEmpty;

    public void RegisterConnection(string connectionId, string? userId = null)
    {
        bool wasEmpty;
        lock (_connectionLock)
        {
            wasEmpty = _connectionSubscriptions.IsEmpty;
            _connectionSubscriptions[connectionId] = userId;
            _connectionVisibility[connectionId] = true;
        }

        if (wasEmpty)
        {
            OnClientsConnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UnregisterConnection(string connectionId)
    {
        bool becameEmpty;
        lock (_connectionLock)
        {
            _connectionSubscriptions.TryRemove(connectionId, out _);
            _connectionVisibility.TryRemove(connectionId, out _);
            becameEmpty = _connectionSubscriptions.IsEmpty;
        }

        if (becameEmpty)
        {
            OnClientsEmpty?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsConnected(string connectionId) => _connectionSubscriptions.ContainsKey(connectionId);

    public void SetConnectionSubscription(string connectionId, string? targetUserId)
    {
        if (_connectionSubscriptions.ContainsKey(connectionId))
        {
            _connectionSubscriptions[connectionId] = targetUserId;
            OnSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? GetConnectionSubscription(string connectionId)
    {
        return _connectionSubscriptions.TryGetValue(connectionId, out string? target) ? target : null;
    }

    public void SetConnectionVisibility(string connectionId, bool isVisible)
    {
        _connectionVisibility[connectionId] = isVisible;
    }

    public bool IsUserVisible(string userId)
    {
        bool hasConnections = false;
        foreach (KeyValuePair<string, string?> pair in _connectionSubscriptions)
        {
            if (string.Equals(pair.Value, userId, StringComparison.Ordinal))
            {
                hasConnections = true;
                if (!_connectionVisibility.TryGetValue(pair.Key, out bool isVis) || isVis)
                {
                    return true;
                }
            }
        }

        return !hasConnections;
    }

    public IReadOnlySet<string> GetActiveUserIdsWithConnectedClients()
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        foreach (string? userId in _connectionSubscriptions.Values)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                set.Add(userId);
            }
        }
        return set;
    }

    public void UpdateUserState(
        string userId,
        string displayName,
        PlaybackState? playbackState,
        SyncedLyrics? lyrics,
        int offsetMs)
    {
        UserPlaybackSnapshot snapshot = new(
            userId,
            displayName,
            playbackState,
            lyrics,
            offsetMs,
            DateTimeOffset.UtcNow);

        _userSnapshots[userId] = snapshot;
    }

    public void UpdateUserPlayback(string userId, string displayName, PlaybackState? playbackState)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _userSnapshots.AddOrUpdate(
            userId,
            _ => new UserPlaybackSnapshot(userId, displayName, playbackState, null, 0, now),
            (_, existing) => existing with
            {
                DisplayName = displayName,
                PlaybackState = playbackState,
                LastUpdatedUtc = now
            });
    }

    public IReadOnlyList<string> ReplaceLyricsForTrack(string trackId, SyncedLyrics? lyrics)
    {
        List<string> updatedUserIds = new();
        foreach (string userId in _userSnapshots.Keys)
        {
            // Compare-and-swap: when another write lands between the read and
            // the swap, re-read so the track check runs on the newer snapshot.
            while (_userSnapshots.TryGetValue(userId, out UserPlaybackSnapshot? existing)
                && string.Equals(existing.PlaybackState?.CurrentTrack?.Id, trackId, StringComparison.Ordinal))
            {
                if (_userSnapshots.TryUpdate(userId, existing with { Lyrics = lyrics }, existing))
                {
                    updatedUserIds.Add(userId);
                    break;
                }
            }
        }

        return updatedUserIds;
    }

    public UserPlaybackSnapshot? GetUserState(string userId)
    {
        return _userSnapshots.TryGetValue(userId, out UserPlaybackSnapshot? snapshot) ? snapshot : null;
    }

    public UserPlaybackSnapshot? GetActivePlaybackSnapshot()
    {
        // First priority: A user who is actively playing
        UserPlaybackSnapshot? playingSnapshot = _userSnapshots.Values
            .FirstOrDefault(s => s.PlaybackState is not null && s.PlaybackState.IsPlaying);

        if (playingSnapshot is not null)
        {
            return playingSnapshot;
        }

        // Second priority: Most recently updated user with a track
        return _userSnapshots.Values
            .Where(s => s.PlaybackState?.CurrentTrack is not null)
            .OrderByDescending(s => s.LastUpdatedUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<UserPlaybackSnapshot> GetAllSnapshots()
    {
        return _userSnapshots.Values.ToList();
    }
}
