using System;
using System.IO;

namespace Cantus.Client.Services;

public interface ILocalServerStateStore
{
    /// <summary>The control token of the server this app last started, or null when it started none.</summary>
    string? ReadControlToken();

    void SaveControlToken(string controlToken);

    void Clear();
}

/// <summary>Keeps the control token in the server's data folder, readable by this OS user only where the OS allows.</summary>
public sealed class LocalServerStateStore : ILocalServerStateStore
{
    public const string STATE_FILE = "local-server.token";

    private readonly string _path;

    public LocalServerStateStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, STATE_FILE);
    }

    public string? ReadControlToken()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            string token = File.ReadAllText(_path).Trim();

            return token.Length > 0 ? token : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveControlToken(string controlToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, controlToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
