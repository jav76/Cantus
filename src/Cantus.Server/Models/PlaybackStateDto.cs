namespace Cantus.Server.Models;

public sealed record PlaybackStateDto
{
    public const string SOURCE_SPOTIFY = "Spotify";
    public const string SOURCE_LOCAL = "Local";

    public TrackInfoDto? CurrentTrack { get; init; }
    public long ProgressMs { get; init; }
    public bool IsPlaying { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public string? DeviceName { get; init; }
    public int? VolumePercent { get; init; }
    public bool IsShuffled { get; init; }
    public string RepeatMode { get; init; } = "off";
    public string? ActiveUserId { get; init; }
    public string? ActiveUserDisplayName { get; init; }

    /// <summary><see cref="SOURCE_SPOTIFY"/> or <see cref="SOURCE_LOCAL"/>.</summary>
    public string Source { get; init; } = SOURCE_SPOTIFY;

    /// <summary>The local player's controls. Null for Spotify playback.</summary>
    public PlaybackCapabilitiesDto? Capabilities { get; init; }
}
