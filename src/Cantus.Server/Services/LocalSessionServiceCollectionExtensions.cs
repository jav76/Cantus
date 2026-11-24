using System.Threading.RateLimiting;
using Cantus.Infrastructure.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Services;

public static class LocalSessionServiceCollectionExtensions
{
    /// <summary>
    /// Registers Local Session settings, the publish ingest and applier, and the creation rate
    /// limit: a fixed window per client address, which never uses the request path.
    /// </summary>
    public static IServiceCollection AddLocalSessions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LocalSessionOptions>()
            .Bind(configuration.GetSection(LocalSessionOptions.SECTION_NAME))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPlaybackUpdateApplier, PlaybackUpdateApplier>();
        services.AddSingleton<ILocalPlaybackIngest, LocalPlaybackIngest>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LocalSessionOptions.RATE_LIMIT_POLICY, GetClientAddressPartition);
        });

        return services;
    }

    private static RateLimitPartition<string> GetClientAddressPartition(HttpContext httpContext)
    {
        LocalSessionOptions localOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<LocalSessionOptions>>()
            .Value;
        string partitionKey = ClientAddressPartition.GetKey(httpContext.Connection.RemoteIpAddress);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = localOptions.RateLimitPermitLimit,
            Window = TimeSpan.FromSeconds(localOptions.RateLimitWindowSeconds),
            QueueLimit = 0
        });
    }
}
