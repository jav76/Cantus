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

        RootCommand rootCommand = new("Cantus Desktop Client")
        {
            logConfigOption
        };
        rootCommand.TreatUnmatchedTokensAsErrors = false;

        ParseResult parseResult = rootCommand.Parse(args);
        string? logConfigValue = parseResult.GetValue(logConfigOption);
        string? logConfigRaw = !string.IsNullOrWhiteSpace(logConfigValue)
            ? logConfigValue
            : Environment.GetEnvironmentVariable("CANTUS_LOG_CONFIGURATION");

        LoggingConfiguration loggingConfig = ClientLoggingManager.ParseConfiguration(logConfigRaw);
        App.InitializeLogging(loggingConfig);

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
