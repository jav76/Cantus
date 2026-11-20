using System;
using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public sealed class SessionTokenResolver : ISessionTokenResolver
{
    private const string COOKIE_NAME = "cantus_session_id";
    private const string ACCESS_TOKEN_QUERY_PARAM = "access_token";
    private const string TOKEN_QUERY_PARAM = "token";
    private const string SESSION_ID_QUERY_PARAM = "session_id";
    private const string AUTHORIZATION_HEADER = "Authorization";
    private const string BEARER_PREFIX = "Bearer ";
    private const string DISPLAY_TOKEN_COOKIE_NAME = "cantus_display_token";
    private const string DISPLAY_TOKEN_QUERY_PARAM = "display_token";

    public string? ResolveSessionToken(HttpContext? httpContext)
    {
        return ResolveSessionId(httpContext);
    }

    public string? ResolveSessionId(HttpContext? httpContext)
    {
        IReadOnlyList<string> tokens = ResolveSharedTokens(httpContext);
        return tokens.Count > 0 ? tokens[0] : null;
    }

    public IReadOnlyList<string> ResolveSharedTokens(HttpContext? httpContext)
    {
        List<string> tokens = new();
        if (httpContext is null)
        {
            return tokens;
        }

        // 1. Check HTTP-only Session Cookie
        if (httpContext.Request.Cookies.TryGetValue(COOKIE_NAME, out string? cookieSessionId))
        {
            AddToken(tokens, cookieSessionId);
        }

        // 2. Check Query String (for WebSockets / SignalR negotiation / SSE)
        AddToken(tokens, httpContext.Request.Query[ACCESS_TOKEN_QUERY_PARAM].ToString());
        AddToken(tokens, httpContext.Request.Query[TOKEN_QUERY_PARAM].ToString());
        AddToken(tokens, httpContext.Request.Query[SESSION_ID_QUERY_PARAM].ToString());

        // 3. Check Authorization header (Bearer <token> or raw token)
        string headerVal = httpContext.Request.Headers[AUTHORIZATION_HEADER].ToString().Trim();
        if (headerVal.StartsWith(BEARER_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            AddToken(tokens, headerVal.Substring(BEARER_PREFIX.Length));
        }
        else
        {
            AddToken(tokens, headerVal);
        }

        return tokens;
    }

    public string? ResolveDisplayToken(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.Request.Cookies.TryGetValue(DISPLAY_TOKEN_COOKIE_NAME, out string? cookieToken) &&
            !string.IsNullOrWhiteSpace(cookieToken))
        {
            return cookieToken.Trim();
        }

        if (httpContext.Request.Query.TryGetValue(DISPLAY_TOKEN_QUERY_PARAM, out Microsoft.Extensions.Primitives.StringValues queryToken) &&
            !string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken.ToString().Trim();
        }

        return null;
    }

    private static void AddToken(List<string> tokens, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string token = value.Trim();
        if (!tokens.Contains(token, StringComparer.Ordinal))
        {
            tokens.Add(token);
        }
    }
}
