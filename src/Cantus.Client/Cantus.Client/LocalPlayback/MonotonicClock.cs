using System;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Reads a <see cref="TimeProvider"/>'s high-resolution timestamp as a <see cref="TimeSpan"/> from an
/// arbitrary origin, so local player times survive wall-clock steps.
/// </summary>
public static class MonotonicClock
{
    public static TimeSpan Now(TimeProvider clock) => clock.GetElapsedTime(0);
}
