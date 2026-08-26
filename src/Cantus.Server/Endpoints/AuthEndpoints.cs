using System.Security.Cryptography;
using Cantus.Core.Interfaces;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace Cantus.Server.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/spotify/login", (
            [FromQuery] bool? json,
            ISpotifyAuthService authService,
            IHostUrlResolver hostUrlResolver,
            HttpContext context) =>
        {
            string state = Guid.NewGuid().ToString("N");
            string verifier = PkceHelper.GenerateCodeVerifier();
            string challenge = PkceHelper.GenerateCodeChallenge(verifier);
            string redirectUri = hostUrlResolver.ResolveSpotifyRedirectUri(context);

            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(10)
            };

            context.Response.Cookies.Append("cantus_oauth_state", state, cookieOptions);
            context.Response.Cookies.Append("cantus_pkce_verifier", verifier, cookieOptions);
            context.Response.Cookies.Append("cantus_oauth_redirect_uri", redirectUri, cookieOptions);

            var uri = authService.GetAuthorizationUri(state, challenge, redirectUri);

            if (json == true)
            {
                return Results.Ok(new { AuthorizationUrl = uri.ToString() });
            }

            return Results.Redirect(uri.ToString());
        })
        .WithName("SpotifyLogin")
        .WithSummary("Initiates Spotify OAuth2 PKCE login flow.");

        group.MapGet("/spotify/callback", async (
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            ISpotifyAuthService authService,
            IPlaybackSessionRegistry registry,
            IHostUrlResolver hostUrlResolver,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AuthEndpoints");

            if (!string.IsNullOrEmpty(error))
            {
                logger.LogWarning("Spotify OAuth returned error: {Error}", error);
                return Results.Redirect("/?auth=error&message=" + Uri.EscapeDataString(error));
            }

            if (string.IsNullOrEmpty(code))
            {
                return Results.BadRequest("Missing authorization code.");
            }

            context.Request.Cookies.TryGetValue("cantus_oauth_state", out var expectedState);
            context.Request.Cookies.TryGetValue("cantus_pkce_verifier", out var verifier);
            context.Request.Cookies.TryGetValue("cantus_oauth_redirect_uri", out var redirectUri);

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
                var session = await authService.ExchangeCodeAsync(code, verifier, effectiveRedirectUri, cancellationToken);
                registry.UpdateUserState(session.Id, session.DisplayName, null, null, 0);

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

                return Results.Redirect("/?auth=success");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to exchange Spotify authorization code.");
                return Results.Redirect("/?auth=error&message=" + Uri.EscapeDataString(ex.Message));
            }
        })
        .WithName("SpotifyCallback")
        .WithSummary("Handles Spotify OAuth2 PKCE callback.");

        group.MapGet("/sessions", async (
            ISpotifyAuthService authService,
            IPlaybackSessionRegistry registry,
            CancellationToken cancellationToken) =>
        {
            var sessions = await authService.GetAllSessionsAsync(cancellationToken);
            var dtos = sessions.Select(s =>
            {
                var snap = registry.GetUserState(s.Id);
                bool isPlaying = snap?.PlaybackState?.IsPlaying ?? false;
                return s.ToDto(isPlaying);
            }).ToList();

            return Results.Ok(dtos);
        })
        .WithName("GetAuthorizedSessions")
        .WithSummary("Lists all authorized Spotify accounts on this server.");

        group.MapGet("/me", async (
            ISpotifyAuthService authService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!context.Request.Cookies.TryGetValue("cantus_session_id", out var sessionId) || string.IsNullOrEmpty(sessionId))
            {
                return Results.Unauthorized();
            }

            var session = await authService.GetSessionAsync(sessionId, cancellationToken);
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
}
