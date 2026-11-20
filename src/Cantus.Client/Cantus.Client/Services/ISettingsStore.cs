namespace Cantus.Client.Services;

/// <summary>
/// Raw per-device key/value storage for client preferences. Implementations
/// never throw: a missing or unreadable value reads as null and a failed
/// write is dropped.
/// </summary>
public interface ISettingsStore
{
    object? GetValue(string key);

    void SetValue(string key, object value);
}
