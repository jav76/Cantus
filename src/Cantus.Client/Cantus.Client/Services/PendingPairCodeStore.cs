using System;
using System.Globalization;

namespace Cantus.Client.Services;

/// <summary>
/// Holds a Display's pairing code in per-device settings while the owner signs in, because the
/// Spotify sign-in leaves the page and returns without the <c>?pair=</c> link.
/// </summary>
public sealed class PendingPairCodeStore
{
    public const string CODE_KEY = "cantus_pending_pair_code";
    public const string SAVED_AT_KEY = "cantus_pending_pair_saved_at";

    /// <summary>The server's default pairing code lifetime; an older code has expired anyway.</summary>
    public static readonly TimeSpan CODE_LIFETIME = TimeSpan.FromMinutes(5);

    private readonly ISettingsStore _store;

    public PendingPairCodeStore(ISettingsStore store)
    {
        _store = store;
    }

    public void Save(string code, DateTimeOffset nowUtc)
    {
        _store.SetValue(CODE_KEY, code);
        _store.SetValue(SAVED_AT_KEY, nowUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Returns the saved code when it is still fresh, and forgets it either way.</summary>
    public string? Take(DateTimeOffset nowUtc)
    {
        string code = ReadString(CODE_KEY);
        string savedAt = ReadString(SAVED_AT_KEY);
        if (code.Length == 0)
        {
            return null;
        }

        Clear();
        if (!long.TryParse(savedAt, NumberStyles.Integer, CultureInfo.InvariantCulture, out long savedAtMs))
        {
            return null;
        }

        TimeSpan age = nowUtc - DateTimeOffset.FromUnixTimeMilliseconds(savedAtMs);
        return age >= TimeSpan.Zero && age < CODE_LIFETIME ? code : null;
    }

    public void Clear()
    {
        if (ReadString(CODE_KEY).Length == 0)
        {
            return;
        }

        _store.SetValue(CODE_KEY, string.Empty);
        _store.SetValue(SAVED_AT_KEY, string.Empty);
    }

    private string ReadString(string key) => _store.GetValue(key) is string value ? value.Trim() : string.Empty;
}
