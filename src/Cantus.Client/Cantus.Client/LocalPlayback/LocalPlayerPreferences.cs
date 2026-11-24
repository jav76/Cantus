using System;
using System.Collections.Generic;
using System.Linq;
using Cantus.Client.Services;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// The per-player off switch, stored as the set of disabled app IDs. Until the owner changes a switch,
/// <c>playerctld</c> (which mirrors other players) and <c>kdeconnect</c> (a phone's players) are off.
/// </summary>
public sealed class LocalPlayerPreferences
{
    public const string KEY_DISABLED_PLAYERS = "cantus_local_disabled_players";

    public static readonly IReadOnlyList<string> DEFAULT_DISABLED_PLAYERS = ["playerctld", "kdeconnect"];

    private const char SEPARATOR = '\n';

    private readonly ISettingsStore _store;
    private readonly object _cacheLock = new();
    private string? _cachedRaw;
    private IReadOnlyList<string> _cachedDisabled = DEFAULT_DISABLED_PLAYERS;

    public LocalPlayerPreferences(ISettingsStore store)
    {
        _store = store;
    }

    public IReadOnlyList<string> DisabledPlayers
    {
        get
        {
            string? raw = _store.GetValue(KEY_DISABLED_PLAYERS) as string;
            lock (_cacheLock)
            {
                if (raw is null)
                {
                    return DEFAULT_DISABLED_PLAYERS;
                }

                if (!string.Equals(raw, _cachedRaw, StringComparison.Ordinal))
                {
                    _cachedRaw = raw;
                    _cachedDisabled = raw
                        .Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                return _cachedDisabled;
            }
        }
    }

    public bool IsEnabled(string appId) => !DisabledPlayers.Any(entry => Matches(entry, appId));

    public void SetEnabled(string appId, bool enabled)
    {
        List<string> disabled = DisabledPlayers.ToList();
        if (enabled)
        {
            disabled.RemoveAll(entry => Matches(entry, appId));
        }
        else if (!disabled.Contains(appId, StringComparer.OrdinalIgnoreCase))
        {
            disabled.Add(appId);
        }

        _store.SetValue(KEY_DISABLED_PLAYERS, string.Join(SEPARATOR, disabled));
    }

    // An entry also matches a dotted app ID that contains it as a whole segment, so "kdeconnect"
    // covers "org.kde.kdeconnect.app" and the "kdeconnect.mpris_<device>" bus suffixes.
    private static bool Matches(string entry, string appId)
    {
        if (string.Equals(entry, appId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string segment in appId.Split('.'))
        {
            if (string.Equals(segment, entry, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
