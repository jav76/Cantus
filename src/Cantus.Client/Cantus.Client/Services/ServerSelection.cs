namespace Cantus.Client.Services;

/// <summary>Where the server address in use came from, lowest precedence first.</summary>
public enum ServerUrlSource
{
    Default,
    Saved,
    EnvironmentVariable,
    CommandLine,
}

/// <summary>
/// The server a desktop app launch connects to. Precedence: <c>--server-url</c>, then
/// <c>CANTUS_SERVER_URL</c>, then the address saved in settings, then this computer.
/// </summary>
/// <param name="BaseUrl">The server's base URL, without the hub path.</param>
/// <param name="NeedsSetup">True on a first launch: nothing overrides, nothing is saved and the prompt was never skipped.</param>
public sealed record ServerSelection(string BaseUrl, ServerUrlSource Source, bool NeedsSetup)
{
    public string HubUrl => ServerAddress.ToHubUrl(BaseUrl);

    /// <summary>A command line or environment address wins over settings, so settings cannot change it for this launch.</summary>
    public bool IsOverridden => Source is ServerUrlSource.CommandLine or ServerUrlSource.EnvironmentVariable;

    /// <param name="overrideUrl">The command line or environment address, or null when neither gave a usable one.</param>
    public static ServerSelection Resolve(string? overrideUrl, ServerUrlSource overrideSource, ServerSettingsStore settings)
    {
        if (overrideSource is ServerUrlSource.CommandLine or ServerUrlSource.EnvironmentVariable
            && ServerAddress.TryNormalize(overrideUrl, out string overrideBaseUrl))
        {
            return new ServerSelection(overrideBaseUrl, overrideSource, NeedsSetup: false);
        }

        string? saved = settings.SavedServerUrl;
        if (saved is not null)
        {
            return new ServerSelection(saved, ServerUrlSource.Saved, NeedsSetup: false);
        }

        return new ServerSelection(ServerAddress.DEFAULT_BASE_URL, ServerUrlSource.Default, !settings.IsSetupDone);
    }
}
