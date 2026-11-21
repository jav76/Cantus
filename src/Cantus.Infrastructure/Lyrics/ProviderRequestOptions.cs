namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Per-request tags read by <see cref="ProviderRateGateHandler"/>. A request
/// without the lookup tag is now-playing traffic.
/// </summary>
public static class ProviderRequestOptions
{
    public static readonly HttpRequestOptionsKey<bool> IsLookup = new("Cantus.ProviderRequest.IsLookup");

    public static void MarkAsLookup(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(IsLookup, true);
    }

    public static bool IsLookupRequest(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(IsLookup, out bool isLookup) && isLookup;
    }
}
