using System.Collections.Concurrent;
using Cantus.Core.Models;

namespace Cantus.Server.Services;

public sealed class PlaybackSessionRegistry : IPlaybackSessionRegistry
{
    private readonly ConcurrentDictionary<string, string?> _connectionSubscriptions = new();
    private readonly ConcurrentDictionary<string, UserPlaybackSnapshot> _userSnapshots = new();
    private readonly object _connectionLock = new();

    public event EventHandler? OnClientsConnected;
    public event EventHandler? OnClientsEmpty;
    public event EventHandler? OnSessionsChanged;

    public int ConnectedClientsCount => _connectionSubscriptions.Count;
    public bool HasConnectedClients => !_connectionSubscriptions.IsEmpty;

    public void RegisterConnection(string connectionId, string? userId = null)
    {
        bool wasEmpty;
        lock (_connectionLock)
        {
            wasEmpty = _connectionSubscriptions.IsEmpty;
            _connectionSubscriptions[connectionId] = userId;
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
            becameEmpty = _connectionSubscriptions.IsEmpty;
        }

        if (becameEmpty)
        {
            OnClientsEmpty?.Invoke(this, EventArgs.Empty);
        }
    }

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
        OnSessionsChanged?.Invoke(this, EventArgs.Empty);
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
