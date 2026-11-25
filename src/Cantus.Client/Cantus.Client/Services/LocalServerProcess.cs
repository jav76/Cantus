using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Cantus.Client.Services;

/// <summary>What the bundled server is started with.</summary>
/// <param name="BaseUrl">The loopback address the server listens on.</param>
/// <param name="ControlToken">The secret that lets this app stop the server later.</param>
public sealed record LocalServerLaunch(LocalServerBundle Bundle, string BaseUrl, string ControlToken)
{
    public IReadOnlyList<string> Arguments => new[] { "--contentRoot", Bundle.ServerDirectory };

    /// <summary>
    /// The server's settings for a per-user install: loopback only, data under the user's profile,
    /// and this computer's players allowed, since nothing off this computer can reach the server.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["ASPNETCORE_URLS"] = BaseUrl,
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["DATA_DIR"] = Bundle.DataDirectory,
        ["ConnectionStrings__CantusDatabase"] = $"Data Source={Bundle.DatabasePath}",
        ["LocalServer__ControlToken"] = ControlToken,
        ["LocalSessions__Enabled"] = "true",
    };
}

public interface ILocalServerProcess
{
    /// <summary>False when another program already listens on the server's address, so starting it would fail.</summary>
    bool IsAddressFree(string baseUrl);

    /// <summary>Starts the server so that it outlives this app. Throws when it cannot be started.</summary>
    void StartDetached(LocalServerLaunch launch);
}

public sealed class LocalServerProcess : ILocalServerProcess
{
    private const string SHELL_PATH = "/bin/sh";

    // nohup and the background job detach the server from this app's terminal and process group.
    private const string DETACH_SCRIPT = "nohup \"$0\" \"$@\" >/dev/null 2>&1 &";

    public bool IsAddressFree(string baseUrl)
    {
        Uri address = new(baseUrl);
        try
        {
            using TcpListener listener = new(IPAddress.Loopback, address.Port);
            listener.Start();

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void StartDetached(LocalServerLaunch launch)
    {
        Directory.CreateDirectory(launch.Bundle.DataDirectory);

        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(launch.Bundle.ExecutablePath)
            : CreateShellStartInfo(launch.Bundle.ExecutablePath);

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.WorkingDirectory = launch.Bundle.DataDirectory;
        foreach (string argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> variable in launch.Environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Could not start {launch.Bundle.ExecutablePath}.");
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(string executablePath)
    {
        ProcessStartInfo startInfo = new(SHELL_PATH);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(DETACH_SCRIPT);
        startInfo.ArgumentList.Add(executablePath);

        return startInfo;
    }
}
