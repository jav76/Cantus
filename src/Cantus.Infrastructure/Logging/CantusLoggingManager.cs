using System;
using System.IO;
using System.Reflection;
using Cantus.Core.Logging;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace Cantus.Infrastructure.Logging;

public static class CantusLoggingManager
{
    private static readonly object _syncLock = new();
    private static LoggingConfiguration _currentConfiguration = LoggingConfiguration.None;
    private static bool _isInitialized;

    public static LoggingConfiguration CurrentConfiguration => _currentConfiguration;

    public static bool IsInitialized => _isInitialized;

    public static string DefaultLogDirectory => Path.Combine(Path.GetTempPath(), "cantus", "logs");

#if DEBUG
    public const LoggingConfiguration DEFAULT_CONFIGURATION = LoggingConfiguration.Debug;
#else
    public const LoggingConfiguration DEFAULT_CONFIGURATION = LoggingConfiguration.None;
#endif

    public static LoggingConfiguration DefaultConfiguration => DEFAULT_CONFIGURATION;

    public static LoggingConfiguration ParseConfiguration(string? value)
    {
        return ParseConfiguration(value, DefaultConfiguration);
    }

    public static LoggingConfiguration ParseConfiguration(string? value, LoggingConfiguration defaultConfiguration)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultConfiguration;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out LoggingConfiguration parsed))
        {
            return parsed;
        }

        return defaultConfiguration;
    }

    public static void InitializeServer(
        LoggingConfiguration configuration,
        string? dbConnectionString = null,
        string? logDirectory = null)
    {
        lock (_syncLock)
        {
            _currentConfiguration = configuration;
            ConfigureHierarchy(
                appName: "server",
                configuration: configuration,
                dbConnectionString: dbConnectionString,
                logDirectory: logDirectory);
            _isInitialized = true;
        }
    }

    public static void InitializeClient(
        LoggingConfiguration configuration,
        string? logDirectory = null)
    {
        lock (_syncLock)
        {
            _currentConfiguration = configuration;
            ConfigureHierarchy(
                appName: "client",
                configuration: configuration,
                dbConnectionString: null,
                logDirectory: logDirectory);
            _isInitialized = true;
        }
    }

    private static void ConfigureHierarchy(
        string appName,
        LoggingConfiguration configuration,
        string? dbConnectionString,
        string? logDirectory)
    {
        Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository(typeof(CantusLoggingManager).Assembly);
        hierarchy.Root.RemoveAllAppenders();

        // 1. Console / Stdout Appender (All configurations)
        PatternLayout consoleLayout = new()
        {
            ConversionPattern = "%date{yyyy-MM-dd HH:mm:ss.fff} [%thread] %-5level %logger - %message%newline%exception"
        };
        consoleLayout.ActivateOptions();

        ConsoleAppender consoleAppender = new()
        {
            Name = "ConsoleAppender",
            Layout = consoleLayout,
            Target = ConsoleAppender.ConsoleOut
        };
        consoleAppender.ActivateOptions();
        hierarchy.Root.AddAppender(consoleAppender);

        // 2. Rolling File & DB Appenders for Debug and Trace modes
        if (configuration is LoggingConfiguration.Debug or LoggingConfiguration.Trace)
        {
            string targetDir = logDirectory ?? DefaultLogDirectory;
            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string logFilePath = Path.Combine(targetDir, $"cantus-{appName}.log");

                PatternLayout fileLayout = new()
                {
                    ConversionPattern = "%date{yyyy-MM-dd HH:mm:ss.fff} [%thread] %-5level %logger - %message%newline%exception"
                };
                fileLayout.ActivateOptions();

                RollingFileAppender fileAppender = new()
                {
                    Name = "RollingFileAppender",
                    File = logFilePath,
                    AppendToFile = true,
                    RollingStyle = RollingFileAppender.RollingMode.Composite,
                    DatePattern = ".yyyy-MM-dd",
                    MaxSizeRollBackups = 10,
                    MaximumFileSize = "10MB",
                    StaticLogFileName = true,
                    Layout = fileLayout
                };
                fileAppender.ActivateOptions();
                hierarchy.Root.AddAppender(fileAppender);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CantusLoggingManager] Warning: Failed to initialize file appender at '{targetDir}': {ex.Message}");
            }

            // 3. SQLite Database Appender for Server
            if (!string.IsNullOrWhiteSpace(dbConnectionString))
            {
                try
                {
                    SqliteBatchLogAppender dbAppender = new()
                    {
                        Name = "SqliteBatchLogAppender",
                        ConnectionString = dbConnectionString
                    };
                    dbAppender.ActivateOptions();
                    hierarchy.Root.AddAppender(dbAppender);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[CantusLoggingManager] Warning: Failed to initialize SQLite log appender: {ex.Message}");
                }
            }
        }

        // Set Root Log Level
        hierarchy.Root.Level = configuration switch
        {
            LoggingConfiguration.None => Level.Info,
            LoggingConfiguration.Debug => Level.Debug,
            LoggingConfiguration.Trace => Level.All,
            _ => Level.Info
        };

        hierarchy.Configured = true;
    }

    public static void Shutdown()
    {
        lock (_syncLock)
        {
            LogManager.Shutdown();
            _isInitialized = false;
        }
    }
}
