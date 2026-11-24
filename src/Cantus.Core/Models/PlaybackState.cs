namespace Cantus.Core.Models;

public sealed record PlaybackState
{
    public TrackInfo? CurrentTrack { get; init; }
    public TimeSpan Progress { get; init; }
    public bool IsPlaying { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public string? DeviceName { get; init; }
    public int? VolumePercent { get; init; }
    public bool IsShuffled { get; init; }

    /// <summary>Spotify repeat state: "off", "track", or "context".</summary>
    public string RepeatMode { get; init; } = "off";

    public PlaybackSource Source { get; init; } = PlaybackSource.Spotify;

    /// <summary>The player's reported rate. Stored for Local playback; viewers project at 1x.</summary>
    public double PlaybackRate { get; init; } = 1.0;

    /// <summary>The controls a local player supports. Null for Spotify playback.</summary>
    public PlaybackCapabilities? Capabilities { get; init; }
}
