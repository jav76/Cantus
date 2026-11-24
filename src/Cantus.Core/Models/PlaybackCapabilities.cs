namespace Cantus.Core.Models;

/// <summary>The transport controls a local player reports it supports.</summary>
public sealed record PlaybackCapabilities
{
    public static PlaybackCapabilities None { get; } = new();

    public bool CanPlay { get; init; }
    public bool CanPause { get; init; }
    public bool CanGoNext { get; init; }
    public bool CanGoPrevious { get; init; }
    public bool CanSeek { get; init; }
}
