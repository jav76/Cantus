using System;
using System.CommandLine;
using Cantus.Client.Services;
using Cantus.Core.Logging;
using Uno.UI.Hosting;

namespace Cantus.Client;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Option<string> logConfigOption = new("--log-configuration", "-l")
        {
            Description = "Specify logging configuration level (none, debug, trace)."
        };

        Option<string> serverUrlOption = new("--server-url", "-s")
        {
            Description = "Base URL of the Cantus server, e.g. http://192.168.0.10:5000."
        };

        RootCommand rootCommand = new("Cantus Desktop Client")
        {
            logConfigOption,
            serverUrlOption
        };
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        ParseResult parseResult = rootCommand.Parse(args);
        string? logConfigValue = parseResult.GetValue(logConfigOption);
        string? logConfigRaw = !string.IsNullOrWhiteSpace(logConfigValue)
            ? logConfigValue
            : Environment.GetEnvironmentVariable("CANTUS_LOG_CONFIGURATION");

        LoggingConfiguration loggingConfig = ClientLoggingManager.ParseConfiguration(logConfigRaw);
        App.InitializeLogging(loggingConfig);

        // Without this the desktop client always targets localhost:5000, so it
        // cannot reach a Cantus server on another machine - and Spotify no
        // longer accepts localhost as a redirect URI host, which left the
        // desktop login flow with nowhere to go.
        string? serverUrlValue = parseResult.GetValue(serverUrlOption);
        string? serverUrlRaw = !string.IsNullOrWhiteSpace(serverUrlValue)
            ? serverUrlValue
            : Environment.GetEnvironmentVariable("CANTUS_SERVER_URL");

        if (!string.IsNullOrWhiteSpace(serverUrlRaw) && !ClientStartupOptions.TrySetServerUrl(serverUrlRaw))
        {
            Console.Error.WriteLine(
                "Ignoring invalid --server-url value: expected an absolute http(s) URL.");
        }

        UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build()
            .Run();
    }
}
