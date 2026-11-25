using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cantus.Server.Endpoints;

/// <summary>
/// Lets the desktop app that started this server stop it. Mapped only when the app passed a
/// control token at start-up, so a server run any other way has no such endpoint.
/// </summary>
public static class LocalServerEndpoints
{
    public const string CONTROL_TOKEN_KEY = "LocalServer:ControlToken";
    public const string CONTROL_TOKEN_HEADER = "X-Cantus-Local-Token";
    public const string SHUTDOWN_PATH = "/api/local-server/shutdown";

    public static IEndpointRouteBuilder MapLocalServerEndpoints(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration)
    {
        string? controlToken = configuration[CONTROL_TOKEN_KEY];
        if (string.IsNullOrWhiteSpace(controlToken))
        {
            return endpoints;
        }

        byte[] expectedToken = Encoding.UTF8.GetBytes(controlToken);

        endpoints.MapPost(SHUTDOWN_PATH, (HttpContext context, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory) =>
            {
                if (!IsFromThisComputer(context) || !HasToken(context, expectedToken))
                {
                    return Results.Unauthorized();
                }

                loggerFactory.CreateLogger(nameof(LocalServerEndpoints)).LogInformation("Stopping at the desktop app's request.");
                lifetime.StopApplication();

                return Results.Accepted();
            })
            .WithName("StopLocalServer")
            .WithSummary("Stops a server that the desktop app started on this computer.")
            .ExcludeFromDescription();

        return endpoints;
    }

    // An in-memory test server has no remote address; a real connection always does.
    private static bool IsFromThisComputer(HttpContext context)
    {
        IPAddress? remoteAddress = context.Connection.RemoteIpAddress;

        return remoteAddress is null || IPAddress.IsLoopback(remoteAddress);
    }

    private static bool HasToken(HttpContext context, byte[] expectedToken)
    {
        string? presented = context.Request.Headers[CONTROL_TOKEN_HEADER];

        return !string.IsNullOrEmpty(presented)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expectedToken);
    }
}
