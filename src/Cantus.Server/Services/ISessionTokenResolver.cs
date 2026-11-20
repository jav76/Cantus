using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public interface ISessionTokenResolver
{
    string? ResolveSessionToken(HttpContext? httpContext);
    string? ResolveSessionId(HttpContext? httpContext);

    /// <summary>
    /// Reads every non-empty value from the shared credential slots, in the order
    /// <see cref="ResolveSessionId"/> checks them: the cantus_session_id cookie, the
    /// access_token, token and session_id query parameters, then the Authorization header.
    /// </summary>
    IReadOnlyList<string> ResolveSharedTokens(HttpContext? httpContext);

    /// <summary>
    /// Reads a Display token from its dedicated slots: the cantus_display_token cookie or the
    /// display_token query parameter.
    /// </summary>
    string? ResolveDisplayToken(HttpContext? httpContext);
}
