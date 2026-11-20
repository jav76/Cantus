using System.Globalization;
using System.Net;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Thrown by <see cref="ProviderRateGateHandler"/> instead of sending a
/// request while the provider is cooling down or the lookup budget is spent.
/// Derives from <see cref="HttpRequestException"/> so every existing caller
/// treats it as "provider unavailable".
/// </summary>
public sealed class ProviderRateLimitedException : HttpRequestException
{
    public ProviderRateLimitedException(string providerName, TimeSpan retryAfter)
        : base(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} requests are paused for {1:F0} s.",
                providerName,
                retryAfter.TotalSeconds),
            null,
            HttpStatusCode.TooManyRequests)
    {
        ProviderName = providerName;
        RetryAfter = retryAfter;
    }

    public string ProviderName { get; }

    public TimeSpan RetryAfter { get; }
}
