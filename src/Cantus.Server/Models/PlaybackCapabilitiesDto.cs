namespace Cantus.Server.Models;

/// <summary>The transport controls a local player supports. Sent with Local playback only.</summary>
public sealed record PlaybackCapabilitiesDto
{
    public bool CanPlay { get; init; }
    public bool CanPause { get; init; }
    public bool CanGoNext { get; init; }
    public bool CanGoPrevious { get; init; }
    public bool CanSeek { get; init; }
}
