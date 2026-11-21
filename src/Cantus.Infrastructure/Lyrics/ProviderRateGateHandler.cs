namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Passes every request of one provider's typed client through
/// <see cref="ProviderRateGate"/>. Added per client with
/// <c>AddHttpMessageHandler(sp =&gt; new ProviderRateGateHandler(...))</c> so
/// each pipeline gets its own instance; never register it as a service.
/// </summary>
public sealed class ProviderRateGateHandler : DelegatingHandler
{
    private readonly string _providerName;
    private readonly ProviderRateGate _gate;

    public ProviderRateGateHandler(string providerName, ProviderRateGate gate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(gate);

        _providerName = providerName;
        _gate = gate;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        bool isLookup = ProviderRequestOptions.IsLookupRequest(request);
        if (!_gate.TryAcquire(_providerName, isLookup, out TimeSpan retryAfter))
        {
            throw new ProviderRateLimitedException(_providerName, retryAfter);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        _gate.ReportResponse(_providerName, isLookup, response.StatusCode, response.Headers.RetryAfter);
        return response;
    }
}
