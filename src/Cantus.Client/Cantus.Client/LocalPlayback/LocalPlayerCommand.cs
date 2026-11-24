namespace Cantus.Client.LocalPlayback;

public enum LocalPlayerCommand
{
    PlayPause,
    Play,
    Pause,
    Next,
    Previous,

    /// <summary>Moves to an absolute position, given in milliseconds.</summary>
    Seek,
}
