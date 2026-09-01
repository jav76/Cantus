using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public interface ISessionTokenResolver
{
    string? ResolveSessionToken(HttpContext? httpContext);
    string? ResolveSessionId(HttpContext? httpContext);
}
