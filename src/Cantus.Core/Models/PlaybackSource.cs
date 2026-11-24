namespace Cantus.Core.Models;

/// <summary>Where a playback state came from: the Spotify Web API poll, or a local player publish.</summary>
public enum PlaybackSource
{
    Spotify,
    Local
}
