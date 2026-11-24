using System;
using Cantus.Client.Models;

namespace Cantus.Client.Services;

public sealed class LyricsPreferences : ILyricsPreferences
{
    public const int DEFAULT_LATENCY_COMPENSATION_MS = 200;
    public const int MIN_LATENCY_COMPENSATION_MS = 0;
    public const int MAX_LATENCY_COMPENSATION_MS = 1000;

    private const string KEY_AUTOSCROLL = "cantus_autoscroll";
    private const string KEY_KARAOKE = "cantus_karaoke";
    private const string KEY_LATENCY = "cantus_latency_ms";
    private const string KEY_FILM_GRAIN = "cantus_fx_film_grain";
    private const string KEY_VIGNETTE = "cantus_fx_vignette";
    private const string KEY_SCANLINES = "cantus_fx_scanlines";
    private const string KEY_BACKGROUND_BLUR = "cantus_fx_background_blur";
    private const string KEY_VU_METER = "cantus_fx_vu_meter";
    private const string KEY_VU_METER_SOURCE = "cantus_vu_meter_source";
    private const bool DEFAULT_AUTOSCROLL_ENABLED = true;
    private const bool DEFAULT_KARAOKE_ENABLED = true;
    private const bool DEFAULT_EFFECT_ENABLED = false;

    private readonly ISettingsStore _store;

    public LyricsPreferences(ISettingsStore store)
    {
        _store = store;
    }

    public bool IsAutoScrollEnabled
    {
        get => ReadBool(KEY_AUTOSCROLL, DEFAULT_AUTOSCROLL_ENABLED);
        set => _store.SetValue(KEY_AUTOSCROLL, value);
    }

    public bool IsKaraokeModeEnabled
    {
        get => ReadBool(KEY_KARAOKE, DEFAULT_KARAOKE_ENABLED);
        set => _store.SetValue(KEY_KARAOKE, value);
    }

    public bool IsFilmGrainEnabled
    {
        get => ReadBool(KEY_FILM_GRAIN, DEFAULT_EFFECT_ENABLED);
        set => _store.SetValue(KEY_FILM_GRAIN, value);
    }

    public bool IsVignetteEnabled
    {
        get => ReadBool(KEY_VIGNETTE, DEFAULT_EFFECT_ENABLED);
        set => _store.SetValue(KEY_VIGNETTE, value);
    }

    public bool IsScanlinesEnabled
    {
        get => ReadBool(KEY_SCANLINES, DEFAULT_EFFECT_ENABLED);
        set => _store.SetValue(KEY_SCANLINES, value);
    }

    public bool IsBackgroundBlurEnabled
    {
        get => ReadBool(KEY_BACKGROUND_BLUR, DEFAULT_EFFECT_ENABLED);
        set => _store.SetValue(KEY_BACKGROUND_BLUR, value);
    }

    public bool IsVuMeterEnabled
    {
        get => ReadBool(KEY_VU_METER, true);
        set => _store.SetValue(KEY_VU_METER, value);
    }

    public VuMeterSource? VuMeterSource
    {
        get => _store.GetValue(KEY_VU_METER_SOURCE) is string s && Enum.TryParse(s, out VuMeterSource source) && Enum.IsDefined(source)
            ? source
            : null;
        set => _store.SetValue(KEY_VU_METER_SOURCE, value?.ToString() ?? string.Empty);
    }

    public int LatencyCompensationMs
    {
        get
        {
            object? value = _store.GetValue(KEY_LATENCY);
            if (value is int i)
            {
                return ClampLatency(i);
            }

            if (value is string s && int.TryParse(s, out int parsed))
            {
                return ClampLatency(parsed);
            }

            return DEFAULT_LATENCY_COMPENSATION_MS;
        }
        set => _store.SetValue(KEY_LATENCY, ClampLatency(value));
    }

    public static int ClampLatency(int value) =>
        Math.Clamp(value, MIN_LATENCY_COMPENSATION_MS, MAX_LATENCY_COMPENSATION_MS);

    private bool ReadBool(string key, bool defaultValue)
    {
        object? value = _store.GetValue(key);
        if (value is bool b)
        {
            return b;
        }

        if (value is string s && bool.TryParse(s, out bool parsed))
        {
            return parsed;
        }

        return defaultValue;
    }
}
