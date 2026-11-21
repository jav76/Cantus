namespace Cantus.Client.Services;

/// <summary>The Display token this device was paired with, and the Display it names.</summary>
public sealed record DisplayCredential(string DisplayId, string Name, string Token)
{
    public override string ToString() =>
        $"DisplayCredential {{ DisplayId = {DisplayId}, Name = {Name}, Token = [REDACTED] }}";
}

/// <summary>
/// Keeps this device's Display token in per-device settings (browser storage on
/// WASM, local settings on desktop), so a paired Display reconnects after a restart.
/// </summary>
public sealed class DisplayCredentialStore
{
    public const string TOKEN_KEY = "cantus_display_token";
    public const string DISPLAY_ID_KEY = "cantus_display_id";
    public const string DISPLAY_NAME_KEY = "cantus_display_name";

    private readonly ISettingsStore _store;

    public DisplayCredentialStore(ISettingsStore store)
    {
        _store = store;
    }

    /// <summary>The stored credential, or null when this device is not a Display.</summary>
    public DisplayCredential? Load()
    {
        string token = ReadString(TOKEN_KEY);
        if (token.Length == 0)
        {
            return null;
        }

        return new DisplayCredential(ReadString(DISPLAY_ID_KEY), ReadString(DISPLAY_NAME_KEY), token);
    }

    public void Save(DisplayCredential credential)
    {
        _store.SetValue(DISPLAY_ID_KEY, credential.DisplayId);
        _store.SetValue(DISPLAY_NAME_KEY, credential.Name);
        _store.SetValue(TOKEN_KEY, credential.Token);
    }

    /// <summary>Forgets the credential. The store cannot remove keys, so each is blanked.</summary>
    public void Clear()
    {
        _store.SetValue(TOKEN_KEY, string.Empty);
        _store.SetValue(DISPLAY_ID_KEY, string.Empty);
        _store.SetValue(DISPLAY_NAME_KEY, string.Empty);
    }

    private string ReadString(string key) => _store.GetValue(key) is string value ? value.Trim() : string.Empty;
}
