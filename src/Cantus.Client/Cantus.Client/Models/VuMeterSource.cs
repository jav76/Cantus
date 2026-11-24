namespace Cantus.Client.Models;

/// <summary>
/// Where the seek bar VU meter gets its levels. <see cref="Decorative"/> is a
/// beat-like pattern that only looks like audio; the others analyse real sound.
/// Which live sources exist depends on the platform.
/// </summary>
public enum VuMeterSource
{
    Decorative,

    /// <summary>Whatever this device is playing (PulseAudio/PipeWire monitor or WASAPI loopback).</summary>
    SystemAudio,

    /// <summary>The microphone, so a display can react to speakers in the room. Browser only.</summary>
    Microphone,

    /// <summary>Audio from a browser tab or the screen, chosen in the browser's share picker.</summary>
    SharedAudio,
}

/// <summary>Whether the meter is currently showing real levels or the decorative pattern.</summary>
public enum VuMeterMode
{
    Decorative,
    Live,
}

public static class VuMeterSourceExtensions
{
    public static string GetDisplayName(this VuMeterSource source) => source switch
    {
        VuMeterSource.Decorative => "Decorative (no audio)",
        VuMeterSource.SystemAudio => "System audio",
        VuMeterSource.Microphone => "Microphone",
        VuMeterSource.SharedAudio => "Tab or screen audio",
        _ => source.ToString(),
    };
}
