using System;

namespace Cantus.Client.LocalPlayback;

public static partial class LocalPlayerSources
{
    // Windows (SMTC) arrives in a later slice; until then it gets the null source too.
    static partial void CreatePlatformSource(ref ILocalPlayerSource? source)
    {
        if (OperatingSystem.IsLinux())
        {
            source = new MprisPlayerSource(TimeProvider.System);
        }
    }
}
