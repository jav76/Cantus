using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public interface ISessionTokenResolver
{
    string? ResolveSessionToken(HttpContext? httpContext);
    string? ResolveSessionId(HttpContext? httpContext);

    /// <summary>
    /// Reads a Display token from its dedicated slots: the cantus_display_token cookie or the
    /// display_token query parameter.
    /// </summary>
    string? ResolveDisplayToken(HttpContext? httpContext);
}
