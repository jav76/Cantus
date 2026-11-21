using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Cantus.Server.Tests.TestSupport;

/// <summary>
/// Records everything a log call could write out: the rendered message, every structured
/// value (which a sink such as the LogEntries table may store separately) and the exception text.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public string AllText => string.Join(Environment.NewLine, _entries);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        _entries.Enqueue($"[scope] {state}");
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        List<string> parts = [$"[{logLevel}] {formatter(state, exception)}"];

        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            parts.AddRange(values.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        if (exception is not null)
        {
            parts.Add(exception.ToString());
        }

        _entries.Enqueue(string.Join(" | ", parts));
    }
}
