using System;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// What a local player reports as its current track. <see cref="AlbumArtUrl"/> is http(s) only, and
/// <see cref="SpotifyTrackId"/> is set only when the player exposes a valid 22-character Spotify track ID.
/// </summary>
public sealed record LocalTrack(
    string Title,
    string Artist,
    string? Album,
    TimeSpan Duration,
    string? AlbumArtUrl,
    string? SpotifyTrackId);
