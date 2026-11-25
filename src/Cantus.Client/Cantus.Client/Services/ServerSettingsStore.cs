namespace Cantus.Client.Services;

/// <summary>
/// The Cantus server this desktop app was told to use, kept in per-device settings, and whether
/// the first-run server prompt has been answered.
/// </summary>
public sealed class ServerSettingsStore
{
    public const string SERVER_URL_KEY = "cantus_server_url";
    public const string SETUP_DONE_KEY = "cantus_server_setup_done";

    private readonly ISettingsStore _store;

    public ServerSettingsStore(ISettingsStore store)
    {
        _store = store;
    }

    /// <summary>The saved base URL, or null when none is saved or the saved value is not a usable address.</summary>
    public string? SavedServerUrl =>
        _store.GetValue(SERVER_URL_KEY) is string raw && ServerAddress.TryNormalize(raw, out string baseUrl)
            ? baseUrl
            : null;

    /// <summary>True once the user saved a server or skipped the prompt, so it is not shown again.</summary>
    public bool IsSetupDone
    {
        get
        {
            if (SavedServerUrl is not null)
            {
                return true;
            }

            object? value = _store.GetValue(SETUP_DONE_KEY);

            return value is true || (value is string text && bool.TryParse(text, out bool parsed) && parsed);
        }
    }

    /// <summary>Saves a server address. Returns false, saving nothing, when it is not a usable address.</summary>
    public bool Save(string serverUrl)
    {
        if (!ServerAddress.TryNormalize(serverUrl, out string baseUrl))
        {
            return false;
        }

        _store.SetValue(SERVER_URL_KEY, baseUrl);
        _store.SetValue(SETUP_DONE_KEY, true);

        return true;
    }

    /// <summary>Records that the prompt was skipped. The store cannot remove keys, so any saved address is blanked.</summary>
    public void SkipSetup()
    {
        _store.SetValue(SERVER_URL_KEY, string.Empty);
        _store.SetValue(SETUP_DONE_KEY, true);
    }
}
