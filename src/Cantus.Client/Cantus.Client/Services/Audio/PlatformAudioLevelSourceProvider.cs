using System;
using System.Collections.Generic;
using Cantus.Client.Models;

namespace Cantus.Client.Services.Audio;

/// <summary>
/// The meter sources this device offers. Desktop and WinUI capture system
/// audio (Linux monitor or Windows loopback) and use it by default, since it
/// needs no permission. The browser offers the microphone and shared tab or
/// screen audio, each behind a browser prompt, so it defaults to none.
/// </summary>
public sealed class PlatformAudioLevelSourceProvider : IAudioLevelSourceProvider
{
    public PlatformAudioLevelSourceProvider()
    {
        List<VuMeterSource> sources = [VuMeterSource.None];
#if __WASM__
        foreach (VuMeterSource source in new[] { VuMeterSource.Microphone, VuMeterSource.SharedAudio })
        {
            if (BrowserAudioLevelSource.IsSupported(source))
            {
                sources.Add(source);
            }
        }

        DefaultSource = VuMeterSource.None;
#else
        if (OperatingSystem.IsWindows() || MonitorCaptureLevelSource.IsSupported())
        {
            sources.Add(VuMeterSource.SystemAudio);
        }

        DefaultSource = sources[^1];
#endif
        AvailableSources = sources;
    }

    public IReadOnlyList<VuMeterSource> AvailableSources { get; }

    public VuMeterSource DefaultSource { get; }

    public IAudioLevelSource? Create(VuMeterSource source)
    {
        if (!Contains(source) || source == VuMeterSource.None)
        {
            return null;
        }

#if __WASM__
        return new BrowserAudioLevelSource(source);
#else
        if (OperatingSystem.IsWindows())
        {
            return new WasapiLoopbackLevelSource();
        }

        return new MonitorCaptureLevelSource();
#endif
    }

    private bool Contains(VuMeterSource source)
    {
        foreach (VuMeterSource available in AvailableSources)
        {
            if (available == source)
            {
                return true;
            }
        }

        return false;
    }
}
