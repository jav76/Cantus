using System.Security.Cryptography;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using SpotifyAPI.Web;

namespace Cantus.Server.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/auth").WithTags("Authentication");

        Delegate loginHandler = (
            [FromQuery] bool? json,
            ISpotifyAuthService authService,
            IHostUrlResolver hostUrlResolver,
            HttpContext context) =>
        {
            string state = Guid.NewGuid().ToString("N");
            string verifier = PkceHelper.GenerateCodeVerifier();
            string challenge = PkceHelper.GenerateCodeChallenge(verifier);
            string redirectUri = hostUrlResolver.ResolveSpotifyRedirectUri(context);

            CookieOptions cookieOptions = new()
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(10)
            };

            context.Response.Cookies.Append("cantus_oauth_state", state, cookieOptions);
            context.Response.Cookies.Append("cantus_pkce_verifier", verifier, cookieOptions);
            context.Response.Cookies.Append("cantus_oauth_redirect_uri", redirectUri, cookieOptions);

            Uri uri = authService.GetAuthorizationUri(state, challenge, redirectUri);

            if (json == true)
            {
                return Results.Ok(new { AuthorizationUrl = uri.ToString() });
            }

            return Results.Redirect(uri.ToString());
        };

        group.MapGet("/spotify/login", loginHandler)
            .WithName("SpotifyLogin")
            .WithSummary("Initiates Spotify OAuth2 PKCE login flow.");

        group.MapGet("/login", loginHandler)
            .WithName("AuthLoginAlias")
            .WithSummary("Initiates Spotify OAuth2 PKCE login flow (alias).");

        Delegate callbackHandler = async (
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            ISpotifyAuthService authService,
            IPlaybackSessionRegistry registry,
            IHostUrlResolver hostUrlResolver,
            IHubContext<PlaybackHub, IPlaybackClient> hubContext,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            ILogger logger = loggerFactory.CreateLogger("AuthEndpoints");

            if (!string.IsNullOrEmpty(error))
            {
                logger.LogWarning("Spotify OAuth returned error: {Error}", error);
                return Results.Redirect("/?auth=error&message=" + Uri.EscapeDataString(error));
            }

            if (string.IsNullOrEmpty(code))
            {
                return Results.BadRequest("Missing authorization code.");
            }

            context.Request.Cookies.TryGetValue("cantus_oauth_state", out string? expectedState);
            context.Request.Cookies.TryGetValue("cantus_pkce_verifier", out string? verifier);
            context.Request.Cookies.TryGetValue("cantus_oauth_redirect_uri", out string? redirectUri);

            if (string.IsNullOrEmpty(expectedState) || expectedState != state)
            {
                logger.LogWarning("OAuth state mismatch. Expected {Expected}, got {State}", expectedState, state);
                return Results.BadRequest("Invalid OAuth state parameter.");
            }

            if (string.IsNullOrEmpty(verifier))
            {
                logger.LogWarning("Missing PKCE code verifier cookie.");
                return Results.BadRequest("Missing PKCE code verifier cookie.");
            }

            string effectiveRedirectUri = !string.IsNullOrWhiteSpace(redirectUri)
                ? redirectUri
                : hostUrlResolver.ResolveSpotifyRedirectUri(context);

            try
            {
                UserSession session = await authService.ExchangeCodeAsync(
                    code,
                    verifier,
                    effectiveRedirectUri,
                    cancellationToken);
                registry.UpdateUserState(session.Id, session.DisplayName, null, null, 0);

                // Broadcast updated session list to all connected SignalR clients (including desktop app)
                IReadOnlyList<UserSession> allSessions = await authService.GetAllSessionsAsync(cancellationToken);
                List<AuthorizedSessionDto> dtos = allSessions.Select(s =>
                {
                    UserPlaybackSnapshot? userSnap = registry.GetUserState(s.Id);
                    bool isPlaying = userSnap?.PlaybackState?.IsPlaying ?? false;
                    return s.ToDto(isPlaying);
                }).ToList();
                await hubContext.Clients.All.ReceiveSessions(dtos);

                // Set session cookie
                context.Response.Cookies.Append("cantus_session_id", session.Id, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });

                // Clear OAuth cookies
                context.Response.Cookies.Delete("cantus_oauth_state");
                context.Response.Cookies.Delete("cantus_pkce_verifier");
                context.Response.Cookies.Delete("cantus_oauth_redirect_uri");

                return Results.Content(GenerateAuthSuccessHtml(session), "text/html");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to exchange Spotify authorization code.");
                return Results.Redirect("/?auth=error&message=" + Uri.EscapeDataString(ex.Message));
            }
        };

        group.MapGet("/spotify/callback", callbackHandler)
            .WithName("SpotifyCallback")
            .WithSummary("Handles Spotify OAuth2 PKCE callback.");

        group.MapGet("/callback", callbackHandler)
            .WithName("AuthCallbackAlias")
            .WithSummary("Handles Spotify OAuth2 PKCE callback (alias).");

        group.MapGet("/sessions", async (
            ISpotifyAuthService authService,
            IPlaybackSessionRegistry registry,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            string? sessionId = ResolveSessionId(context);
            if (string.IsNullOrEmpty(sessionId))
            {
                return Results.Ok(Array.Empty<AuthorizedSessionDto>());
            }

            UserSession? session = await authService.GetSessionAsync(sessionId, cancellationToken);
            if (session is null)
            {
                return Results.Ok(Array.Empty<AuthorizedSessionDto>());
            }

            UserPlaybackSnapshot? snap = registry.GetUserState(session.Id);
            bool isPlaying = snap?.PlaybackState?.IsPlaying ?? false;
            return Results.Ok(new List<AuthorizedSessionDto> { session.ToDto(isPlaying) });
        })
        .WithName("GetAuthorizedSessions")
        .WithSummary("Lists the current authorized Spotify account session.");

        group.MapGet("/me", async (
            ISpotifyAuthService authService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            string? sessionId = ResolveSessionId(context);
            if (string.IsNullOrEmpty(sessionId))
            {
                return Results.Unauthorized();
            }

            UserSession? session = await authService.GetSessionAsync(sessionId, cancellationToken);
            if (session is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(session.ToDto());
        })
        .WithName("GetCurrentUser")
        .WithSummary("Gets the currently authenticated session.");

        group.MapDelete("/sessions/{userId}", async (
            string userId,
            ISpotifyAuthService authService,
            CancellationToken cancellationToken) =>
        {
            bool revoked = await authService.RevokeSessionAsync(userId, cancellationToken);
            if (!revoked)
            {
                return Results.NotFound(new { Message = $"Session '{userId}' not found." });
            }

            return Results.Ok(new { Message = $"Session '{userId}' revoked." });
        })
        .WithName("RevokeSession")
        .WithSummary("Revokes an authorized Spotify account session.");

        group.MapPost("/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Delete("cantus_session_id");
            return Results.Ok(new { Message = "Logged out." });
        })
        .WithName("Logout")
        .WithSummary("Clears current user session cookie.");

        return endpoints;
    }

    private static string? ResolveSessionId(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue("cantus_session_id", out string? cookieSessionId) &&
            !string.IsNullOrWhiteSpace(cookieSessionId))
        {
            return cookieSessionId;
        }

        if (context.Request.Query.TryGetValue("access_token", out StringValues queryToken) &&
            !string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken.ToString();
        }

        if (context.Request.Query.TryGetValue("session_id", out StringValues sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId.ToString();
        }

        if (context.Request.Headers.TryGetValue("Authorization", out StringValues authHeader) &&
            !string.IsNullOrWhiteSpace(authHeader))
        {
            string headerStr = authHeader.ToString().Trim();
            if (headerStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return headerStr.Substring(7).Trim();
            }
            return headerStr;
        }

        return null;
    }

    private static string GenerateAuthSuccessHtml(UserSession session)
    {
        string encodedDisplayName = System.Net.WebUtility.HtmlEncode(session.DisplayName);
        string sessionId = session.Id;

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Cantus - Spotify Connected</title>
    <style>
        :root {
            --bg-color: #0f172a;
            --card-bg: #1e293b;
            --accent-green: #10b981;
            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --border-color: rgba(255, 255, 255, 0.1);
        }
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }
        body {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
            background-color: var(--bg-color);
            color: var(--text-primary);
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            padding: 24px;
        }
        .card {
            background-color: var(--card-bg);
            border: 1px solid var(--border-color);
            border-radius: 16px;
            padding: 40px;
            max-width: 480px;
            width: 100%;
            text-align: center;
            box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5), 0 8px 10px -6px rgba(0, 0, 0, 0.5);
        }
        .icon-badge {
            width: 64px;
            height: 64px;
            background: rgba(16, 185, 129, 0.15);
            border: 2px solid var(--accent-green);
            border-radius: 50%;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            margin: 0 auto 24px auto;
            color: var(--accent-green);
            font-size: 32px;
            font-weight: bold;
        }
        h1 {
            font-size: 24px;
            font-weight: 700;
            margin-bottom: 8px;
            color: var(--text-primary);
        }
        .user-name {
            color: var(--accent-green);
            font-weight: 600;
        }
        p {
            color: var(--text-secondary);
            font-size: 15px;
            line-height: 1.5;
            margin-bottom: 28px;
        }
        .btn-group {
            display: flex;
            flex-direction: column;
            gap: 12px;
        }
        .btn {
            display: inline-block;
            padding: 12px 24px;
            border-radius: 8px;
            font-size: 15px;
            font-weight: 600;
            text-decoration: none;
            cursor: pointer;
            transition: all 0.2s ease;
        }
        .btn-primary {
            background-color: var(--accent-green);
            color: #042f1f;
            border: none;
        }
        .btn-primary:hover {
            background-color: #34d399;
            transform: translateY(-1px);
        }
        .btn-secondary {
            background: rgba(255, 255, 255, 0.05);
            color: var(--text-primary);
            border: 1px solid var(--border-color);
        }
        .btn-secondary:hover {
            background: rgba(255, 255, 255, 0.1);
        }
    </style>
</head>
<body>
    <div class="card">
        <div class="icon-badge">&#10003;</div>
        <h1>Spotify Connected</h1>
        <p>Logged in as <span class="user-name">{{encodedDisplayName}}</span>.<br>You can now return to the Cantus Desktop App, or continue in the Web Player.</p>
        <div class="btn-group">
            <a href="cantus://auth?session_id={{sessionId}}" class="btn btn-primary" id="open-app-btn">Return to Desktop App</a>
            <a href="/?auth=success" class="btn btn-secondary">Open Web Player</a>
        </div>
    </div>
    <script>
        try {
            window.location.href = "cantus://auth?session_id={{sessionId}}";
        } catch (e) {
        }
    </script>
</body>
</html>
""";
    }
}
