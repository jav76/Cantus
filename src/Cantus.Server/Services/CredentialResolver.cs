using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Cantus.Server.Services;

public sealed class CredentialResolver : ICredentialResolver
{
    private readonly ISessionTokenResolver _tokenResolver;
    private readonly ISpotifyAuthService _authService;
    private readonly IDisplayService _displayService;

    public CredentialResolver(
        ISessionTokenResolver tokenResolver,
        ISpotifyAuthService authService,
        IDisplayService displayService)
    {
        _tokenResolver = tokenResolver;
        _authService = authService;
        _displayService = displayService;
    }

    public async Task<CallerCredential?> ResolveAsync(
        HttpContext? httpContext,
        CancellationToken cancellationToken = default)
    {
        if (httpContext is null)
        {
            return null;
        }

        string? sessionCandidate = _tokenResolver.ResolveSessionId(httpContext);
        if (!string.IsNullOrWhiteSpace(sessionCandidate))
        {
            UserSession? session = await _authService.GetSessionByIdAsync(sessionCandidate, cancellationToken);
            if (session is not null)
            {
                return new CallerCredential(CredentialKind.Session, session, null);
            }
        }

        // The shared bearer slots (access_token, Authorization) may carry a Display token too,
        // because the SignalR client sends whatever its access token factory returns there.
        string? displayCandidate = _tokenResolver.ResolveDisplayToken(httpContext) ?? sessionCandidate;
        if (string.IsNullOrWhiteSpace(displayCandidate))
        {
            return null;
        }

        DisplayInfo? display = await _displayService.ValidateAsync(displayCandidate, cancellationToken);
        if (display is null)
        {
            return null;
        }

        UserSession? owner = await _authService.GetSessionByIdAsync(display.OwnerSessionId, cancellationToken);
        if (owner is null)
        {
            return null;
        }

        return new CallerCredential(CredentialKind.Display, owner, display);
    }
}
