using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
using Cantus.Infrastructure.Sessions;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Endpoints;

public static class AuthEndpoints
{
    public const string USER_CLIENT_ID_QUERY = "spotify_client_id";
    private const string USER_CLIENT_ID_COOKIE = "cantus_spotify_client_id";
    public const string SESSION_COOKIE = "cantus_session_id";
    public const string ERROR_LOCAL_SESSIONS_DISABLED = "local_sessions_disabled";
    public const string ERROR_JOIN_CODE_REQUIRED = "join_code_required";
    public const string ERROR_JOIN_CODE_INVALID = "join_code_invalid";
    public const string ERROR_INVALID_DISPLAY_NAME = "invalid_display_name";
    public const string ERROR_LOCAL_SESSION_LIMIT = "local_session_limit";

    private const int SESSION_COOKIE_LIFETIME_DAYS = 30;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/spotify/login", HandleLogin)
            .WithName("SpotifyLogin")
            .WithSummary("Initiates Spotify OAuth2 PKCE login flow.");

        group.MapGet("/login", HandleLogin)
            .WithName("AuthLoginAlias")
            .WithSummary("Initiates Spotify OAuth2 PKCE login flow (alias).");

        group.MapGet("/spotify/callback", HandleCallback)
            .WithName("SpotifyCallback")
            .WithSummary("Handles Spotify OAuth2 PKCE callback.");

        group.MapGet("/callback", HandleCallback)
            .WithName("AuthCallbackAlias")
            .WithSummary("Handles Spotify OAuth2 PKCE callback (alias).");

        group.MapGet("/spotify/setup", HandleGetSetupInfo)
            .WithName("SpotifySetupInfo")
            .WithSummary("Describes how this server signs in to Spotify, for the Connect Spotify setup screen.");

        group.MapGet("/sessions", HandleGetSessions)
            .WithName("GetAuthorizedSessions")
            .WithSummary("Lists the current authorized Spotify account session.");

        group.MapGet("/me", HandleGetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Gets the currently authenticated session.");

        group.MapDelete("/sessions/{userId}", HandleRevokeSession)
            .WithName("RevokeSession")
            .WithSummary("Revokes an authorized Spotify account session.");

        group.MapPost("/local", HandleCreateLocalSession)
            .RequireRateLimiting(LocalSessionOptions.RATE_LIMIT_POLICY)
            .WithName("CreateLocalSession")
            .WithSummary("Creates a Local Session, which shows what this computer's own players are playing.");

        group.MapPost("/logout", HandleLogout)
            .WithName("Logout")
            .WithSummary("Clears current user session cookie and revokes session.");

        return endpoints;
    }

    private static IResult HandleLogin(
        [FromQuery] bool? json,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = USER_CLIENT_ID_QUERY)] string? requestedUserClientId,
        ISpotifyAuthService authService,
        IHostUrlResolver hostUrlResolver,
        IOptions<SpotifyOptions> spotifyOptions,
        HttpContext context)
    {
        SpotifyOptions options = spotifyOptions.Value;
        string redirectUri = hostUrlResolver.ResolveSpotifyRedirectUri(context);

        IResult? clientIdFailure = TryValidateUserClientId(requestedUserClientId, redirectUri, options, out string? userClientId);
        if (clientIdFailure is not null)
        {
            return clientIdFailure;
        }

        string state = Guid.NewGuid().ToString("N");
        string verifier = PkceHelper.GenerateCodeVerifier();
        string challenge = PkceHelper.GenerateCodeChallenge(verifier);

        Uri uri;
        try
        {
            uri = authService.GetAuthorizationUri(state, challenge, redirectUri, userClientId);
        }
        catch (SpotifyNotConfiguredException)
        {
            return FailurePage(SpotifyAuthFailure.NotConfigured, redirectUri, options, StatusCodes.Status503ServiceUnavailable);
        }

        CookieOptions cookieOptions = new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        context.Response.Cookies.Append("cantus_oauth_state", state, cookieOptions);
        context.Response.Cookies.Append("cantus_pkce_verifier", verifier, cookieOptions);
        context.Response.Cookies.Append("cantus_oauth_redirect_uri", redirectUri, cookieOptions);

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            context.Response.Cookies.Append("cantus_client_id", clientId, cookieOptions);
        }

        // Always written, so a stale cookie from an earlier own-app attempt cannot
        // carry over into a sign-in through the server's app.
        if (userClientId is not null)
        {
            context.Response.Cookies.Append(USER_CLIENT_ID_COOKIE, userClientId, cookieOptions);
        }
        else
        {
            context.Response.Cookies.Delete(USER_CLIENT_ID_COOKIE);
        }

        if (json == true)
        {
            return Results.Ok(new { AuthorizationUrl = uri.ToString() });
        }

        return Results.Redirect(uri.ToString());
    }

    private static IResult HandleGetSetupInfo(
        IHostUrlResolver hostUrlResolver,
        IOptions<SpotifyOptions> spotifyOptions,
        HttpContext context)
    {
        SpotifyOptions options = spotifyOptions.Value;
        string redirectUri = hostUrlResolver.ResolveSpotifyRedirectUri(context);
        return Results.Ok(new SpotifySetupInfoDto
        {
            ServerClientIdConfigured = options.ServerClientIdConfigured,
            UserClientIdsAllowed = options.UserClientIdsAllowed,
            RedirectUri = redirectUri,
            RedirectUriAccepted = IsRedirectUriAcceptedBySpotify(redirectUri),
        });
    }

    /// <summary>
    /// Spotify accepts https anywhere, and plain http only on a loopback IP literal
    /// (<c>127.0.0.1</c> or <c>[::1]</c>), never <c>localhost</c>.
    /// </summary>
    internal static bool IsRedirectUriAcceptedBySpotify(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            && uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            && System.Net.IPAddress.TryParse(uri.IdnHost, out System.Net.IPAddress? address)
            && System.Net.IPAddress.IsLoopback(address);
    }

    private static IResult FailurePage(
        SpotifyAuthFailure failure,
        string? redirectUri,
        SpotifyOptions options,
        int statusCode)
    {
        return Results.Content(
            AuthPages.Failure(failure, redirectUri, options.UserClientIdsAllowed),
            "text/html",
            statusCode: statusCode);
    }

    private static async Task<IResult> HandleCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        ISpotifyAuthService authService,
        IPlaybackSessionRegistry registry,
        IHostUrlResolver hostUrlResolver,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        IOptions<SpotifyOptions> spotifyOptions,
        HttpContext context,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("AuthEndpoints");
        SpotifyOptions options = spotifyOptions.Value;

        if (!string.IsNullOrEmpty(error))
        {
            logger.LogWarning("Spotify OAuth returned error: {Error}", error);
            SpotifyAuthFailure callbackFailure = SpotifyAuthFailureClassifier.FromCallbackError(error);
            return FailurePage(
                callbackFailure,
                null,
                options,
                callbackFailure == SpotifyAuthFailure.Cancelled ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrEmpty(code))
        {
            return Results.BadRequest("Missing authorization code.");
        }

        context.Request.Cookies.TryGetValue("cantus_oauth_state", out string? expectedState);
        context.Request.Cookies.TryGetValue("cantus_pkce_verifier", out string? verifier);
        context.Request.Cookies.TryGetValue("cantus_oauth_redirect_uri", out string? redirectUri);
        context.Request.Cookies.TryGetValue("cantus_client_id", out string? clientId);
        context.Request.Cookies.TryGetValue(USER_CLIENT_ID_COOKIE, out string? userClientIdCookie);

        if (string.IsNullOrEmpty(expectedState) || expectedState != state)
        {
            logger.LogWarning("OAuth state mismatch. Expected {Expected}, got {State}", expectedState, state);
            return FailurePage(SpotifyAuthFailure.SignInExpired, null, options, StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrEmpty(verifier))
        {
            logger.LogWarning("Missing PKCE code verifier cookie.");
            return FailurePage(SpotifyAuthFailure.SignInExpired, null, options, StatusCodes.Status400BadRequest);
        }

        IResult? clientIdFailure = TryValidateUserClientId(userClientIdCookie, null, options, out string? userClientId);
        if (clientIdFailure is not null)
        {
            return clientIdFailure;
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
                userClientId,
                cancellationToken);
            // Preserve any in-flight snapshot: re-linking mid-song must not wipe the
            // current playback state, lyrics, or saved track offset, because the
            // poller only rebroadcasts lyrics on a track change.
            UserPlaybackSnapshot? existingSnapshot = registry.GetUserState(session.Id);
            registry.UpdateUserState(
                session.Id,
                session.DisplayName,
                existingSnapshot?.PlaybackState,
                existingSnapshot?.Lyrics,
                existingSnapshot?.TrackOffsetMs ?? 0);

            bool isPlaying = existingSnapshot?.PlaybackState?.IsPlaying ?? false;
            AuthorizedSessionDto dto = session.ToDto(isPlaying);

            await BroadcastSessionAsync(hubContext, clientId, session.Id, dto);

            AppendSessionCookie(context, session.Id);

            context.Response.Cookies.Delete("cantus_oauth_state");
            context.Response.Cookies.Delete("cantus_pkce_verifier");
            context.Response.Cookies.Delete("cantus_oauth_redirect_uri");
            context.Response.Cookies.Delete("cantus_client_id");
            context.Response.Cookies.Delete(USER_CLIENT_ID_COOKIE);

            return Results.Content(AuthPages.Success(session), "text/html");
        }
        catch (Exception ex)
        {
            SpotifyAuthFailure failure = SpotifyAuthFailureClassifier.FromException(ex);
            logger.LogError(ex, "Failed to exchange Spotify authorization code ({Failure}).", failure);
            return FailurePage(
                failure,
                effectiveRedirectUri,
                options,
                failure == SpotifyAuthFailure.Unknown ? StatusCodes.Status502BadGateway : StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandleGetSessions(
        IPlaybackSessionRegistry registry,
        ICredentialResolver credentialResolver,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is null)
        {
            return Results.Ok(Array.Empty<AuthorizedSessionDto>());
        }

        // The session list carries the owner's Session id, which is itself a credential.
        if (!caller.IsSession)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        UserSession session = caller.Owner;
        UserPlaybackSnapshot? snap = registry.GetUserState(session.Id);
        bool isPlaying = snap?.PlaybackState?.IsPlaying ?? false;
        return Results.Ok(new List<AuthorizedSessionDto> { session.ToDto(isPlaying) });
    }

    private static async Task<IResult> HandleGetCurrentUser(
        ICredentialResolver credentialResolver,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is null)
        {
            return Results.Unauthorized();
        }

        if (!caller.IsSession)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(caller.Owner.ToDto());
    }

    private static async Task<IResult> HandleRevokeSession(
        string userId,
        ISpotifyAuthService authService,
        ISessionTokenResolver sessionResolver,
        ICredentialResolver credentialResolver,
        IDisplayService displayService,
        IDisplayConnectionTracker displayConnections,
        IPlaybackSessionRegistry registry,
        ILocalPlaybackIngest localIngest,
        IPlaybackUpdateApplier applier,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is { IsSession: false })
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // A session id is itself the credential here, so only the holder of a
        // session may revoke it. Without this check any caller that can reach
        // the server can revoke any session by id.
        string? callerSessionId = sessionResolver.ResolveSessionId(context);
        if (string.IsNullOrEmpty(callerSessionId))
        {
            return Results.Unauthorized();
        }

        if (!string.Equals(callerSessionId, userId, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        bool revoked = await EndSessionAsync(
            userId,
            new SessionEndServices(authService, displayService, displayConnections, registry, localIngest, applier),
            cancellationToken);
        if (!revoked)
        {
            return Results.NotFound(new { Message = $"Session '{userId}' not found." });
        }

        await hubContext.Clients.Group($"user_{userId}").ReceiveSessionRevoked(userId);
        await hubContext.Clients.Group($"user_{userId}").ReceiveSessions(Array.Empty<AuthorizedSessionDto>());

        return Results.Ok(new { Message = $"Session '{userId}' revoked." });
    }

    private static async Task<IResult> HandleLogout(
        ISpotifyAuthService authService,
        ISessionTokenResolver sessionResolver,
        ICredentialResolver credentialResolver,
        IDisplayService displayService,
        IDisplayConnectionTracker displayConnections,
        IPlaybackSessionRegistry registry,
        ILocalPlaybackIngest localIngest,
        IPlaybackUpdateApplier applier,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is { IsSession: false })
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        string? sessionId = sessionResolver.ResolveSessionId(context);
        if (!string.IsNullOrEmpty(sessionId))
        {
            await EndSessionAsync(
                sessionId,
                new SessionEndServices(authService, displayService, displayConnections, registry, localIngest, applier),
                cancellationToken);
            await hubContext.Clients.Group($"user_{sessionId}").ReceiveSessionRevoked(sessionId);
            await hubContext.Clients.Group($"user_{sessionId}").ReceiveSessions(Array.Empty<AuthorizedSessionDto>());
        }

        context.Response.Cookies.Delete(SESSION_COOKIE);
        return Results.Ok(new { Message = "Logged out and session revoked." });
    }

    /// <summary>
    /// Revokes a Session, drops its playback state and closes the live connections of its
    /// Displays, which the database deletes along with it. Returns false when the Session does
    /// not exist.
    /// </summary>
    private static async Task<bool> EndSessionAsync(
        string sessionId,
        SessionEndServices services,
        CancellationToken cancellationToken)
    {
        // Listed before the deletion removes them, but closed only after it, so a closed
        // Display that reconnects presents a token that no longer validates.
        IReadOnlyList<DisplayInfo> displays = await services.Displays.ListAsync(sessionId, cancellationToken);

        bool revoked = await services.Auth.RevokeSessionAsync(sessionId, cancellationToken);
        if (!revoked)
        {
            return false;
        }

        services.LocalIngest.Forget(sessionId);
        services.Applier.Forget(sessionId);
        services.Registry.RemoveUserState(sessionId);

        foreach (DisplayInfo display in displays)
        {
            await services.DisplayConnections.DisconnectAsync(display.Id);
        }

        return true;
    }

    /// <summary>
    /// Creates a Local Session: no Spotify account, playback published by the desktop client.
    /// Off unless the operator enables it; optionally gated by a join code compared in constant time.
    /// </summary>
    private static async Task<IResult> HandleCreateLocalSession(
        [FromBody] LocalSessionRequestDto? request,
        IOptions<LocalSessionOptions> options,
        ILocalSessionService localSessions,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        LocalSessionOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return Error(StatusCodes.Status403Forbidden, ERROR_LOCAL_SESSIONS_DISABLED);
        }

        if (settings.RequiresJoinCode)
        {
            if (string.IsNullOrEmpty(request?.JoinCode))
            {
                return Error(StatusCodes.Status401Unauthorized, ERROR_JOIN_CODE_REQUIRED);
            }

            if (!JoinCodeMatches(request.JoinCode, settings.JoinCode))
            {
                return Error(StatusCodes.Status401Unauthorized, ERROR_JOIN_CODE_INVALID);
            }
        }

        LocalSessionCreateResult result = await localSessions.CreateAsync(request?.DisplayName, cancellationToken);
        if (result is not { Status: LocalSessionCreateStatus.Created, Session: { } session })
        {
            return result.Status == LocalSessionCreateStatus.InvalidDisplayName
                ? Error(StatusCodes.Status400BadRequest, ERROR_INVALID_DISPLAY_NAME)
                : Error(StatusCodes.Status503ServiceUnavailable, ERROR_LOCAL_SESSION_LIMIT);
        }

        AppendSessionCookie(context, session.Id);
        return Results.Ok(session.ToDto());
    }

    /// <summary>
    /// Compares SHA-256 digests in constant time, so neither the code's content nor its length
    /// leaks through timing.
    /// </summary>
    private static bool JoinCodeMatches(string candidate, string expected)
    {
        byte[] candidateHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate));
        byte[] expectedHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(candidateHash, expectedHash);
    }

    private static IResult Error(int statusCode, string error) =>
        Results.Json(new ApiErrorDto(error), statusCode: statusCode);

    private static void AppendSessionCookie(HttpContext context, string sessionId)
    {
        context.Response.Cookies.Append(SESSION_COOKIE, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(SESSION_COOKIE_LIFETIME_DAYS)
        });
    }

    private sealed record SessionEndServices(
        ISpotifyAuthService Auth,
        IDisplayService Displays,
        IDisplayConnectionTracker DisplayConnections,
        IPlaybackSessionRegistry Registry,
        ILocalPlaybackIngest LocalIngest,
        IPlaybackUpdateApplier Applier);

    private static IResult? TryValidateUserClientId(
        string? rawClientId,
        string? redirectUri,
        SpotifyOptions options,
        out string? userClientId)
    {
        userClientId = null;
        if (string.IsNullOrWhiteSpace(rawClientId))
        {
            return null;
        }

        if (!options.UserClientIdsAllowed)
        {
            return FailurePage(SpotifyAuthFailure.UserClientIdsDisabled, redirectUri, options, StatusCodes.Status403Forbidden);
        }

        if (!SpotifyClientIdFormat.TryNormalize(rawClientId, out userClientId))
        {
            return FailurePage(SpotifyAuthFailure.InvalidClientId, redirectUri, options, StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static async Task BroadcastSessionAsync(
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        string? clientId,
        string sessionId,
        AuthorizedSessionDto dto)
    {
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            await hubContext.Clients.Group($"client_{clientId}").ReceiveAuthSession(dto);
            await hubContext.Clients.Client(clientId).ReceiveAuthSession(dto);
            await hubContext.Clients.Group($"client_{clientId}").ReceiveSessions(new List<AuthorizedSessionDto> { dto });
        }

        await hubContext.Clients.Group($"user_{sessionId}").ReceiveSessions(new List<AuthorizedSessionDto> { dto });
    }
}
