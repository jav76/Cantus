using System;
using System.IO;

namespace Cantus.Client.Services;

/// <summary>
/// The Cantus server shipped next to the desktop app, and the per-user folder it keeps its data in.
/// </summary>
/// <param name="ExecutablePath">The server executable inside the app's <c>server</c> folder.</param>
/// <param name="DataDirectory">Where the server's database, keys and state file live.</param>
public sealed record LocalServerBundle(string ExecutablePath, string DataDirectory)
{
    public const string SERVER_FOLDER = "server";
    public const string SERVER_NAME = "Cantus.Server";
    public const string DATA_FOLDER = "Cantus";
    public const string DATABASE_FILE = "cantus.db";

    private const string APPIMAGE_VARIABLE = "APPIMAGE";

    public string ServerDirectory => Path.GetDirectoryName(ExecutablePath) ?? string.Empty;

    public string DatabasePath => Path.Combine(DataDirectory, DATABASE_FILE);

    /// <summary>Finds the server shipped with this app, or null when this build has none.</summary>
    public static LocalServerBundle? FindForThisApp()
    {
        // An AppImage unmounts its files when the app exits, which would take a still-running server with it.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(APPIMAGE_VARIABLE)))
        {
            return null;
        }

        string dataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return string.IsNullOrEmpty(dataRoot) ? null : Find(AppContext.BaseDirectory, dataRoot, OperatingSystem.IsWindows());
    }

    public static LocalServerBundle? Find(string appDirectory, string dataRoot, bool isWindows)
    {
        string executable = Path.Combine(appDirectory, SERVER_FOLDER, isWindows ? $"{SERVER_NAME}.exe" : SERVER_NAME);

        return File.Exists(executable)
            ? new LocalServerBundle(executable, Path.Combine(dataRoot, DATA_FOLDER, SERVER_FOLDER))
            : null;
    }
}
