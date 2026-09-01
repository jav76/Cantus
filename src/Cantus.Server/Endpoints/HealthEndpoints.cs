using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Infrastructure.Persistence;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cantus.Server.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health", HandleHealthCheck)
            .WithName("HealthCheck")
            .WithSummary("System liveness and readiness health probe.");

        return endpoints;
    }

    private static async Task<IResult> HandleHealthCheck(
        CantusDbContext dbContext,
        IPlaybackSessionRegistry sessionRegistry,
        CancellationToken cancellationToken)
    {
        bool dbCanConnect = false;
        try
        {
            dbCanConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            dbCanConnect = false;
        }

        string version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        int activeSessions = sessionRegistry.GetAllSnapshots().Count;

        HealthResponseDto response = new(
            Status: dbCanConnect ? "Healthy" : "Degraded",
            Version: version,
            ActiveSessions: activeSessions,
            Database: dbCanConnect ? "Connected" : "Disconnected"
        );

        return dbCanConnect
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

public sealed record HealthResponseDto(
    string Status,
    string Version,
    int ActiveSessions,
    string Database);
