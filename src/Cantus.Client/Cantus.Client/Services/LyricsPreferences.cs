using System;

namespace Cantus.Client.Services;

public sealed class LyricsPreferences : ILyricsPreferences
{
    public const int DEFAULT_LATENCY_COMPENSATION_MS = 200;
    public const int MIN_LATENCY_COMPENSATION_MS = 0;
    public const int MAX_LATENCY_COMPENSATION_MS = 1000;

    private const string KEY_AUTOSCROLL = "cantus_autoscroll";
    private const string KEY_KARAOKE = "cantus_karaoke";
    private const string KEY_LATENCY = "cantus_latency_ms";
    private const bool DEFAULT_AUTOSCROLL_ENABLED = true;
    private const bool DEFAULT_KARAOKE_ENABLED = true;

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
