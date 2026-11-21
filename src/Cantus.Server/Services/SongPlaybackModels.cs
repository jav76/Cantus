using Cantus.Core.Models;

namespace Cantus.Server.Services;

/// <summary>What to do with a song lookup guess on the owner's Spotify device.</summary>
public enum SongPlayAction
{
    Play = 0,
    Queue = 1
}

/// <summary>Outcome of playing or queueing a song lookup guess.</summary>
public enum SongPlayStatus
{
    Success = 0,

    /// <summary>No Spotify search hit was accepted as the guessed song; nothing was sent.</summary>
    NotOnSpotify = 1,

    /// <summary>Spotify returned 404: no active device to control.</summary>
    NoActiveDevice = 2,

    /// <summary>Spotify returned 403: missing playback scope or not a Premium account.</summary>
    MissingPermissions = 3,

    /// <summary>Spotify calls are paused by a recent 429.</summary>
    RateLimited = 4,

    Failed = 5
}

/// <summary>
/// A guess to play or queue: either a library guess by Spotify track id, or a
/// provider guess by title and artist that is resolved on Spotify first.
/// </summary>
/// <param name="Action">Play now or add to the queue.</param>
/// <param name="SpotifyTrackId">A 22-character Spotify track id; takes precedence over the title.</param>
/// <param name="Title">The guessed title, when there is no track id.</param>
/// <param name="Artist">The guessed artist, when there is no track id.</param>
/// <param name="DurationMs">The guessed duration, when the provider knows it.</param>
public sealed record SongPlayRequest(
    SongPlayAction Action,
    string? SpotifyTrackId,
    string? Title = null,
    string? Artist = null,
    int? DurationMs = null);

/// <param name="Status">What happened.</param>
/// <param name="Track">The Spotify track a title and artist resolved to; null for a play by id.</param>
/// <param name="RetryAfter">When rate limited, how long until Spotify may be called again.</param>
public sealed record SongPlayResult(
    SongPlayStatus Status,
    TrackInfo? Track = null,
    TimeSpan? RetryAfter = null);
