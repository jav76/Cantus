using System.Threading.RateLimiting;
using Cantus.Server.Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Services;

public static class DisplayPairingServiceCollectionExtensions
{
    /// <summary>
    /// Registers pending pairings, Display connection tracking and the pairing rate limits.
    /// Requests and approvals each get their own fixed window per client address. The partition
    /// never uses the request path, which callers can vary in case without changing the endpoint.
    /// </summary>
    public static IServiceCollection AddDisplayPairing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DisplayPairingOptions>()
            .Bind(configuration.GetSection(DisplayPairingOptions.SECTION_NAME))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IDisplayPairingService, DisplayPairingService>();
        services.AddSingleton<IDisplayConnectionTracker, DisplayConnectionTracker>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(DisplayPairingOptions.REQUEST_RATE_LIMIT_POLICY, GetClientAddressPartition);
            options.AddPolicy(DisplayPairingOptions.APPROVE_RATE_LIMIT_POLICY, GetClientAddressPartition);
        });

        return services;
    }

    private static RateLimitPartition<string> GetClientAddressPartition(HttpContext httpContext)
    {
        DisplayPairingOptions pairingOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<DisplayPairingOptions>>()
            .Value;
        string partitionKey = ClientAddressPartition.GetKey(httpContext.Connection.RemoteIpAddress);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = pairingOptions.RateLimitPermitLimit,
            Window = TimeSpan.FromSeconds(pairingOptions.RateLimitWindowSeconds),
            QueueLimit = 0
        });
    }
}
