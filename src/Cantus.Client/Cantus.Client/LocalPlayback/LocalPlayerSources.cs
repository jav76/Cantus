namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Picks the local player source for this platform. The desktop head implements
/// <see cref="CreatePlatformSource"/>; every other head and the tests get the null source.
/// </summary>
public static partial class LocalPlayerSources
{
    public static ILocalPlayerSource Create()
    {
        ILocalPlayerSource? source = null;
        CreatePlatformSource(ref source);
        return source ?? new NullLocalPlayerSource();
    }

    static partial void CreatePlatformSource(ref ILocalPlayerSource? source);
}
