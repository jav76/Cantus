using System.Threading.RateLimiting;
using Cantus.Infrastructure.Lyrics.Lookup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Services;

public static class SongLookupServiceCollectionExtensions
{
    public const string SEARCH_RATE_LIMIT_POLICY = "song-lookup-search";
    public const string PLAY_RATE_LIMIT_POLICY = "song-lookup-play";

    /// <summary>
    /// Registers the inbound song lookup rate limits: searches and plays each get their own
    /// fixed window per client address. <see cref="SongLookupOptions"/> is bound and validated
    /// on start by the infrastructure registration.
    /// </summary>
    public static IServiceCollection AddSongLookup(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                SEARCH_RATE_LIMIT_POLICY,
                httpContext => GetClientAddressPartition(httpContext, lookup => lookup.SearchPermitLimit));
            options.AddPolicy(
                PLAY_RATE_LIMIT_POLICY,
                httpContext => GetClientAddressPartition(httpContext, lookup => lookup.PlayPermitLimit));
        });

        return services;
    }

    private static RateLimitPartition<string> GetClientAddressPartition(
        HttpContext httpContext,
        Func<SongLookupOptions, int> permitLimit)
    {
        SongLookupOptions lookupOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<SongLookupOptions>>()
            .Value;
        string partitionKey = ClientAddressPartition.GetKey(httpContext.Connection.RemoteIpAddress);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit(lookupOptions),
            Window = TimeSpan.FromSeconds(lookupOptions.RateLimitWindowSeconds),
            QueueLimit = 0
        });
    }
}
