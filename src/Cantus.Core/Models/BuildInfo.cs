using System;
using System.Reflection;

namespace Cantus.Core.Models;

public static class BuildInfo
{
    private const int SHORT_SHA_LENGTH = 7;
    private const string DEFAULT_VERSION = "0.2.0-dev";
    private const string UNKNOWN_SHA = "unknown";

    static BuildInfo()
    {
        Assembly assembly = typeof(BuildInfo).Assembly;
        AssemblyInformationalVersionAttribute? infoVersionAttr = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        string rawInfoVersion = infoVersionAttr?.InformationalVersion ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawInfoVersion))
        {
            Version? asmVersion = assembly.GetName().Version;
            SemVer = asmVersion is not null ? asmVersion.ToString(3) : DEFAULT_VERSION;
            CommitSha = UNKNOWN_SHA;
            InformationalVersion = SemVer;
        }
        else
        {
            InformationalVersion = rawInfoVersion;
            string[] parts = rawInfoVersion.Split('+');
            SemVer = !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : DEFAULT_VERSION;

            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                string rawSha = parts[1].Trim();
                CommitSha = rawSha.Length >= SHORT_SHA_LENGTH
                    ? rawSha[..SHORT_SHA_LENGTH]
                    : rawSha;
            }
            else
            {
                CommitSha = UNKNOWN_SHA;
            }
        }

#if DEBUG
        IsDebug = true;
        Version = CommitSha is not UNKNOWN_SHA
            ? $"{SemVer}+{CommitSha}"
            : $"{SemVer}+debug";
#else
        IsDebug = false;
        Version = SemVer;
#endif
    }

    public static string Version { get; }

    public static string SemVer { get; }

    public static string CommitSha { get; }

    public static string InformationalVersion { get; }

    public static bool IsDebug { get; }
}
