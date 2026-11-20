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
    private const string UNKNOWN_CLIENT_ADDRESS = "unknown";

    /// <summary>
    /// Registers pending pairings, Display connection tracking and the pairing rate limit.
    /// The limit is a fixed window per client address, counted separately per endpoint path.
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
            options.AddPolicy(DisplayPairingOptions.RATE_LIMIT_POLICY, httpContext =>
            {
                DisplayPairingOptions pairingOptions = httpContext.RequestServices
                    .GetRequiredService<IOptions<DisplayPairingOptions>>()
                    .Value;
                string address = httpContext.Connection.RemoteIpAddress?.ToString() ?? UNKNOWN_CLIENT_ADDRESS;
                string partitionKey = $"{httpContext.Request.Path}|{address}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = pairingOptions.RateLimitPermitLimit,
                    Window = TimeSpan.FromSeconds(pairingOptions.RateLimitWindowSeconds),
                    QueueLimit = 0
                });
            });
        });

        return services;
    }
}
