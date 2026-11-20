using System.Collections.Generic;
using Cantus.Client.Services;

namespace Cantus.Client.Tests.Services;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> _values = new();

    public IReadOnlyDictionary<string, object> Values => _values;

    public object? GetValue(string key) => _values.TryGetValue(key, out object? value) ? value : null;

    public void SetValue(string key, object value)
    {
        _values[key] = value;
    }
}
