namespace Cantus.Client.Models;

/// <summary>
/// Where the seek bar VU meter gets its levels. <see cref="None"/> leaves the
/// seek bar plain; the others analyse real sound. Which live sources exist
/// depends on the platform.
/// </summary>
public enum VuMeterSource
{
    None,

    /// <summary>Whatever this device is playing (PulseAudio/PipeWire monitor or WASAPI loopback).</summary>
    SystemAudio,

    /// <summary>The microphone, so a display can react to speakers in the room. Browser only.</summary>
    Microphone,

    /// <summary>Audio from a browser tab or the screen, chosen in the browser's share picker.</summary>
    SharedAudio,
}

/// <summary>Whether the meter is showing real levels or leaving the seek bar plain.</summary>
public enum VuMeterMode
{
    Static,
    Live,
}

public static class VuMeterSourceExtensions
{
    public static string GetDisplayName(this VuMeterSource source) => source switch
    {
        VuMeterSource.None => "None (plain seek bar)",
        VuMeterSource.SystemAudio => "System audio",
        VuMeterSource.Microphone => "Microphone",
        VuMeterSource.SharedAudio => "Tab or screen audio",
        _ => source.ToString(),
    };
}
