using System;
using Cantus.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

public static class ClientLoggingManager
{
    private static readonly object _syncLock = new();
    private static LoggingConfiguration _currentConfiguration = LoggingConfiguration.None;
    private static bool _isInitialized;
    private static ILoggerFactory? _loggerFactory;

    public static LoggingConfiguration CurrentConfiguration => _currentConfiguration;

    public static bool IsInitialized => _isInitialized;

    public static ILoggerFactory? LoggerFactory => _loggerFactory;

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

            ILoggerFactory factory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
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

            _loggerFactory = factory;
            _isInitialized = true;
            return factory;
        }
    }

    public static ILoggerFactory InitializeLogging(LoggingConfiguration configuration = DEFAULT_CONFIGURATION)
    {
        return CreateLoggerFactory(configuration);
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return new DelegatingClientLogger(categoryName);
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return new DelegatingClientLogger<T>();
    }

    private static ILoggerFactory GetOrCreateLoggerFactory()
    {
        lock (_syncLock)
        {
            _loggerFactory ??= CreateLoggerFactory();
            return _loggerFactory;
        }
    }

    private sealed class DelegatingClientLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly object _cacheLock = new();
        private ILoggerFactory? _cachedFactory;
        private ILogger? _cachedLogger;

        public DelegatingClientLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        private ILogger CurrentLogger
        {
            get
            {
                ILoggerFactory activeFactory = GetOrCreateLoggerFactory();

                if (_cachedLogger is not null && ReferenceEquals(_cachedFactory, activeFactory))
                {
                    return _cachedLogger;
                }

                lock (_cacheLock)
                {
                    if (_cachedLogger is not null && ReferenceEquals(_cachedFactory, activeFactory))
                    {
                        return _cachedLogger;
                    }

                    ILogger logger = activeFactory.CreateLogger(_categoryName);
                    _cachedFactory = activeFactory;
                    _cachedLogger = logger;
                    return logger;
                }
            }
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            CurrentLogger.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return CurrentLogger.IsEnabled(logLevel);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return CurrentLogger.BeginScope(state);
        }
    }

    private sealed class DelegatingClientLogger<T> : ILogger<T>
    {
        private readonly DelegatingClientLogger _inner;

        public DelegatingClientLogger()
        {
            _inner = new DelegatingClientLogger(typeof(T).FullName ?? typeof(T).Name);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _inner.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _inner.IsEnabled(logLevel);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return _inner.BeginScope(state);
        }
    }

    internal static void ResetForTesting()
    {
        lock (_syncLock)
        {
            _loggerFactory = null;
            _isInitialized = false;
            _currentConfiguration = LoggingConfiguration.None;
        }
    }
}
