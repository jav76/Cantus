namespace Cantus.Client.LocalPlayback;

/// <summary>The transport controls a local player accepts right now.</summary>
public sealed record LocalPlayerCapabilities(
    bool CanPlay,
    bool CanPause,
    bool CanGoNext,
    bool CanGoPrevious,
    bool CanSeek)
{
    public static LocalPlayerCapabilities None { get; } = new(false, false, false, false, false);
}
