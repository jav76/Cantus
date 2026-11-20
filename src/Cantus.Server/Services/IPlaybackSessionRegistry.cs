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
    event EventHandler<string>? OnUserActivityRequested;

    void RequestUserActivity(string userId);

    /// <summary>
    /// Like <see cref="RequestUserActivity"/>, but forwards at most one request per owner within
    /// a minimum interval. Displays are view-only screens on shared hardware, so they must not
    /// be able to drive the owner's Spotify quota with a burst of refreshes.
    /// </summary>
    void RequestDisplayActivity(string userId);

    void RegisterConnection(string connectionId, string? userId = null);
    void UnregisterConnection(string connectionId);
    bool IsConnected(string connectionId);
    void SetConnectionSubscription(string connectionId, string? targetUserId);
    string? GetConnectionSubscription(string connectionId);
    void SetConnectionVisibility(string connectionId, bool isVisible);
    bool IsUserVisible(string userId);
    IReadOnlySet<string> GetActiveUserIdsWithConnectedClients();

    void UpdateUserState(
        string userId,
        string displayName,
        PlaybackState? playbackState,
        SyncedLyrics? lyrics,
        int offsetMs);

    /// <summary>
    /// Stores <paramref name="playbackState"/> while keeping the lyrics and
    /// offset the snapshot holds at the moment of the write, so a change made
    /// out of band (a lyrics override, a new offset) during a slow Spotify
    /// call is not overwritten with the values read before it.
    /// </summary>
    void UpdateUserPlayback(string userId, string displayName, PlaybackState? playbackState);

    /// <summary>
    /// Replaces the lyrics of every snapshot whose current track is
    /// <paramref name="trackId"/>, leaving its playback state and offset as
    /// they are. A snapshot that has moved on to another track is skipped.
    /// </summary>
    /// <returns>The ids of the users whose snapshot was updated.</returns>
    IReadOnlyList<string> ReplaceLyricsForTrack(string trackId, SyncedLyrics? lyrics);

    UserPlaybackSnapshot? GetUserState(string userId);
    UserPlaybackSnapshot? GetActivePlaybackSnapshot();
    IReadOnlyList<UserPlaybackSnapshot> GetAllSnapshots();
}
