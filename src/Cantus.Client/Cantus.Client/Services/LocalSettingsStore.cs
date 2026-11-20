namespace Cantus.Client.Services;

/// <summary>
/// <see cref="ISettingsStore"/> backed by <c>ApplicationData.Current.LocalSettings</c>
/// (browser storage on WASM, the app data folder on desktop).
/// </summary>
public sealed class LocalSettingsStore : ISettingsStore
{
    public object? GetValue(string key)
    {
        try
        {
            return Windows.Storage.ApplicationData.Current?.LocalSettings?.Values[key];
        }
        catch
        {
            return null;
        }
    }

    public void SetValue(string key, object value)
    {
        try
        {
            if (Windows.Storage.ApplicationData.Current?.LocalSettings?.Values is { } values)
            {
                values[key] = value;
            }
        }
        catch
        {
        }
    }
}
