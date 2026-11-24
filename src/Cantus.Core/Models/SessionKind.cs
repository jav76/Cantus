namespace Cantus.Core.Models;

/// <summary>
/// What a Session is backed by. A Spotify Session holds Spotify tokens and is polled; a Local
/// Session has no Spotify account and receives playback that a desktop client publishes.
/// </summary>
public enum SessionKind
{
    Spotify,
    Local
}
