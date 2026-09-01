using System;
using Cantus.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

public static class ClientLoggingManager
{
    private static readonly object _syncLock = new();
    private static LoggingConfiguration _currentConfiguration = LoggingConfiguration.None;
    private static bool _isInitialized;

    public static LoggingConfiguration CurrentConfiguration => _currentConfiguration;

    public static bool IsInitialized => _isInitialized;

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

    public static ILoggerFactory CreateLoggerFactory(LoggingConfiguration configuration = DEFAULT_CONFIGURATION)
    {
        lock (_syncLock)
        {
            _currentConfiguration = configuration;

            ILoggerFactory factory = LoggerFactory.Create(builder =>
            {
#if __WASM__
                builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#else
                builder.AddConsole();
#endif
                LogLevel minLevel = configuration switch
                {
                    LoggingConfiguration.None => LogLevel.Information,
                    LoggingConfiguration.Debug => LogLevel.Debug,
                    LoggingConfiguration.Trace => LogLevel.Trace,
                    _ => LogLevel.Information
                };

                builder.SetMinimumLevel(minLevel);
                builder.AddFilter("Uno", LogLevel.Warning);
                builder.AddFilter("Windows", LogLevel.Warning);
                builder.AddFilter("Microsoft", LogLevel.Warning);
            });

            _isInitialized = true;
            return factory;
        }
    }

    public static ILoggerFactory InitializeLogging(LoggingConfiguration configuration = DEFAULT_CONFIGURATION)
    {
        return CreateLoggerFactory(configuration);
    }
}
