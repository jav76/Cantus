using System;
using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public sealed class SessionTokenResolver : ISessionTokenResolver
{
    private const string COOKIE_NAME = "cantus_session_id";
    private const string ACCESS_TOKEN_QUERY_PARAM = "access_token";
    private const string SESSION_ID_QUERY_PARAM = "session_id";
    private const string AUTHORIZATION_HEADER = "Authorization";
    private const string BEARER_PREFIX = "Bearer ";

    public string? ResolveSessionToken(HttpContext? httpContext)
    {
        return ResolveSessionId(httpContext);
    }

    public string? ResolveSessionId(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        // 1. Check HTTP-only Session Cookie
        if (httpContext.Request.Cookies.TryGetValue(COOKIE_NAME, out string? cookieSessionId) &&
            !string.IsNullOrWhiteSpace(cookieSessionId))
        {
            return cookieSessionId.Trim();
        }

        // 2. Check Query String (for WebSockets / SignalR negotiation / SSE)
        if (httpContext.Request.Query.TryGetValue(ACCESS_TOKEN_QUERY_PARAM, out Microsoft.Extensions.Primitives.StringValues accessTokenQuery) &&
            !string.IsNullOrWhiteSpace(accessTokenQuery))
        {
            return accessTokenQuery.ToString().Trim();
        }

        if (httpContext.Request.Query.TryGetValue("token", out Microsoft.Extensions.Primitives.StringValues tokenQuery) &&
            !string.IsNullOrWhiteSpace(tokenQuery))
        {
            return tokenQuery.ToString().Trim();
        }

        if (httpContext.Request.Query.TryGetValue(SESSION_ID_QUERY_PARAM, out Microsoft.Extensions.Primitives.StringValues sessionIdQuery) &&
            !string.IsNullOrWhiteSpace(sessionIdQuery))
        {
            return sessionIdQuery.ToString().Trim();
        }

        // 3. Check Authorization header (Bearer <token> or raw token)
        if (httpContext.Request.Headers.TryGetValue(AUTHORIZATION_HEADER, out Microsoft.Extensions.Primitives.StringValues authHeader) &&
            !string.IsNullOrWhiteSpace(authHeader))
        {
            string headerVal = authHeader.ToString().Trim();
            if (headerVal.StartsWith(BEARER_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                string token = headerVal.Substring(BEARER_PREFIX.Length).Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
            else
            {
                return headerVal;
            }
        }

        return null;
    }
}
