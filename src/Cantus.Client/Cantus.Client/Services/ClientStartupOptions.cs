using System;

namespace Cantus.Client.Services;

/// <summary>What became of the server address given on the command line or in the environment.</summary>
public enum ServerOverrideResult
{
    None,
    Applied,
    InvalidCommandLine,
    InvalidEnvironmentVariable,
}

/// <summary>
/// Startup values supplied by the host process (desktop CLI arguments or
/// environment variables) before any page or view model is constructed.
/// </summary>
public static class ClientStartupOptions
{
    /// <summary>
    /// Fully-qualified SignalR hub URL the client should connect to, or null
    /// to use the default resolution (the page origin on WASM, localhost
    /// otherwise).
    /// </summary>
    public static string? ServerUrl { get; private set; }

    /// <summary>Where <see cref="ServerUrl"/> came from.</summary>
    public static ServerUrlSource ServerUrlSource { get; private set; } = ServerUrlSource.Default;

    /// <summary>The desktop app's resolved server, or null before <see cref="ResolveServer"/> ran (always null on WASM).</summary>
    public static ServerSelection? Selection { get; private set; }

    /// <summary>
    /// Records the Cantus server address the client should connect to.
    /// Accepts either a base address (<c>http://192.168.0.10:5000</c>) or a
    /// full hub URL; the hub path is appended when missing, so callers can
    /// pass the same address they would open in a browser. Any query string or
    /// fragment is dropped. Values that are not absolute http/https URLs are
    /// rejected and leave the default resolution in place rather than
    /// preventing the client from starting.
    /// </summary>
    /// <returns>True when the value was accepted.</returns>
    public static bool TrySetServerUrl(string? rawUrl, ServerUrlSource source = ServerUrlSource.CommandLine)
    {
        if (!ServerAddress.TryNormalize(rawUrl, out string baseUrl))
        {
            return false;
        }

        ServerUrl = ServerAddress.ToHubUrl(baseUrl);
        ServerUrlSource = source;

        return true;
    }

    /// <summary>
    /// Applies <c>--server-url</c> or, when that is blank, <c>CANTUS_SERVER_URL</c>. An invalid value
    /// is ignored and does not fall through to the other one.
    /// </summary>
    public static ServerOverrideResult ApplyServerOverride(string? commandLineValue, string? environmentValue)
    {
        if (!string.IsNullOrWhiteSpace(commandLineValue))
        {
            return TrySetServerUrl(commandLineValue, ServerUrlSource.CommandLine)
                ? ServerOverrideResult.Applied
                : ServerOverrideResult.InvalidCommandLine;
        }

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return TrySetServerUrl(environmentValue, ServerUrlSource.EnvironmentVariable)
                ? ServerOverrideResult.Applied
                : ServerOverrideResult.InvalidEnvironmentVariable;
        }

        return ServerOverrideResult.None;
    }

    /// <summary>
    /// Settles the desktop app's server from the override, the saved setting and the default, and
    /// points <see cref="ServerUrl"/> at it. Safe to call again after the saved setting changes.
    /// </summary>
    public static ServerSelection ResolveServer(ServerSettingsStore settings)
    {
        bool isOverridden = ServerUrlSource is ServerUrlSource.CommandLine or ServerUrlSource.EnvironmentVariable;
        ServerSelection selection = ServerSelection.Resolve(isOverridden ? ServerUrl : null, ServerUrlSource, settings);

        ServerUrl = selection.Source == ServerUrlSource.Default ? null : selection.HubUrl;
        ServerUrlSource = selection.Source;
        Selection = selection;

        return selection;
    }

    internal static void ResetForTesting()
    {
        ServerUrl = null;
        ServerUrlSource = ServerUrlSource.Default;
        Selection = null;
    }
}
