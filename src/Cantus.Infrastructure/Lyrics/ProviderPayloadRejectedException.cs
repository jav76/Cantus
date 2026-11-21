using System.Globalization;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Thrown when a provider answers HTTP 200 but its payload reports an error,
/// a throttle or a challenge (NetEase does this through its root "code").
/// The caller reports it with <see cref="ProviderRateGate.ReportLookupCooldown"/>.
/// </summary>
public sealed class ProviderPayloadRejectedException : HttpRequestException
{
    public ProviderPayloadRejectedException(string providerName, int? payloadCode)
        : base(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} rejected the request with payload code {1}.",
                providerName,
                payloadCode?.ToString(CultureInfo.InvariantCulture) ?? "(none)"))
    {
        ProviderName = providerName;
        PayloadCode = payloadCode;
    }

    public string ProviderName { get; }

    public int? PayloadCode { get; }
}
