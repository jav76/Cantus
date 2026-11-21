using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Cantus.Client.Services;

/// <summary>
/// Thread-safe first-in, first-out cache keyed by URL. Adding past the
/// capacity evicts the oldest entry; adding an existing URL keeps the first value.
/// </summary>
internal sealed class BoundedUrlCache<TValue>
    where TValue : class
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly Dictionary<string, TValue> _valuesByUrl = new(StringComparer.Ordinal);
    private readonly Queue<string> _evictionOrder = new();

    public BoundedUrlCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _valuesByUrl.Count;
            }
        }
    }

    public bool TryGet(string url, [NotNullWhen(true)] out TValue? value)
    {
        lock (_lock)
        {
            return _valuesByUrl.TryGetValue(url, out value);
        }
    }

    public void Add(string url, TValue value)
    {
        lock (_lock)
        {
            if (_valuesByUrl.ContainsKey(url))
            {
                return;
            }

            while (_evictionOrder.Count >= _capacity)
            {
                _valuesByUrl.Remove(_evictionOrder.Dequeue());
            }

            _valuesByUrl[url] = value;
            _evictionOrder.Enqueue(url);
        }
    }
}
