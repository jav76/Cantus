using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cantus.Server.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        string requestMethod = httpContext.Request.Method;
        string requestPath = httpContext.Request.Path.Value ?? string.Empty;
        string queryString = httpContext.Request.QueryString.Value ?? string.Empty;

        _logger.LogError(
            exception,
            "Unhandled server exception occurred while processing HTTP {Method} {Path}{Query} [TraceId: {TraceId}]",
            requestMethod,
            requestPath,
            queryString,
            traceId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        ProblemDetails problemDetails = new()
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = _environment.IsDevelopment()
                ? exception.ToString()
                : "An unexpected server error occurred. Please contact support or provide the trace ID.",
            Instance = requestPath
        };
        problemDetails.Extensions["traceId"] = traceId;

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json; charset=utf-8",
            cancellationToken: cancellationToken);
        return true;
    }
}
