using Cantus.Core.Models;

namespace Cantus.Server.Services;

public sealed record UserPlaybackSnapshot(
    string UserId,
    string DisplayName,
    PlaybackState? PlaybackState,
    SyncedLyrics? Lyrics,
    int TrackOffsetMs,
    DateTimeOffset LastUpdatedUtc);

public interface IPlaybackSessionRegistry
{
    int ConnectedClientsCount { get; }
    bool HasConnectedClients { get; }

    event EventHandler? OnClientsConnected;
    event EventHandler? OnClientsEmpty;
    event EventHandler? OnSessionsChanged;

    void RegisterConnection(string connectionId);
    void UnregisterConnection(string connectionId);
    void SetConnectionSubscription(string connectionId, string? targetUserId);
    string? GetConnectionSubscription(string connectionId);

    void UpdateUserState(
        string userId,
        string displayName,
        PlaybackState? playbackState,
        SyncedLyrics? lyrics,
        int offsetMs);

    UserPlaybackSnapshot? GetUserState(string userId);
    UserPlaybackSnapshot? GetActivePlaybackSnapshot();
    IReadOnlyList<UserPlaybackSnapshot> GetAllSnapshots();
}
