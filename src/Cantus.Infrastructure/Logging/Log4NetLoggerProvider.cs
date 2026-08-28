using System;
using System.Collections.Concurrent;
using Cantus.Core.Logging;
using log4net;
using log4net.Core;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Logging;

public sealed class Log4NetLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, Log4NetLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new Log4NetLogger(name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}

internal sealed class Log4NetLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly log4net.Core.ILogger _logger;

    public Log4NetLogger(string categoryName)
    {
        ILog log = LogManager.GetLogger(typeof(Log4NetLogger).Assembly, categoryName);
        _logger = log.Logger;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        Level log4NetLevel = ToLog4NetLevel(logLevel);
        return _logger.IsEnabledFor(log4NetLevel);
    }

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Level log4NetLevel = ToLog4NetLevel(logLevel);
        if (!_logger.IsEnabledFor(log4NetLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        _logger.Log(typeof(Log4NetLogger), log4NetLevel, message, exception);
    }

    private static Level ToLog4NetLevel(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        return logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Trace => Level.Trace,
            Microsoft.Extensions.Logging.LogLevel.Debug => Level.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => Level.Info,
            Microsoft.Extensions.Logging.LogLevel.Warning => Level.Warn,
            Microsoft.Extensions.Logging.LogLevel.Error => Level.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => Level.Fatal,
            _ => Level.Off
        };
    }
}
