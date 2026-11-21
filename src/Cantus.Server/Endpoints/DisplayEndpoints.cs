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

namespace Cantus.Server.Endpoints;

/// <summary>
/// Display pairing and management. A waiting client requests a pairing code anonymously; an
/// owner with a Session approves it, and the Display token is pushed to the waiting client's
/// hub connection. Every other endpoint is owner-only and rejects Display tokens.
/// </summary>
public static class DisplayEndpoints
{
    private const string LOGGER_CATEGORY = "DisplayEndpoints";

    public static IEndpointRouteBuilder MapDisplayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/displays").WithTags("Displays");

        group.MapPost("/pairings", HandleRequestPairing)
            .RequireRateLimiting(DisplayPairingOptions.REQUEST_RATE_LIMIT_POLICY)
            .WithName("RequestDisplayPairing")
            .WithSummary("Starts pairing a Display: returns a short, single-use code for the owner to approve.");

        group.MapPost("/pairings/approve", HandleApprovePairing)
            .RequireRateLimiting(DisplayPairingOptions.APPROVE_RATE_LIMIT_POLICY)
            .WithName("ApproveDisplayPairing")
            .WithSummary("Approves a pairing code, names the Display and sends its token to the waiting client.");

        group.MapGet("/", HandleList)
            .WithName("ListDisplays")
            .WithSummary("Lists the caller's Displays.");

        group.MapPatch("/{displayId}", HandleRename)
            .WithName("RenameDisplay")
            .WithSummary("Renames one of the caller's Displays.");

        group.MapDelete("/{displayId}", HandleRevoke)
            .WithName("RevokeDisplay")
            .WithSummary("Revokes one of the caller's Displays and closes its live connections.");

        return endpoints;
    }

    private static IResult HandleRequestPairing(
        [FromBody] DisplayPairingRequestDto request,
        IDisplayPairingService pairings,
        IPlaybackSessionRegistry registry)
    {
        string? connectionId = request.ConnectionId;
        if (string.IsNullOrWhiteSpace(connectionId) || !registry.IsConnected(connectionId))
        {
            return Results.BadRequest(new { Message = "ConnectionId must be a live PlaybackHub connection." });
        }

        PendingDisplayPairing? pairing = pairings.Request(connectionId);
        if (pairing is null)
        {
            return Results.Problem(
                "Too many pairings are waiting for approval. Try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new DisplayPairingDto
        {
            PairingId = pairing.PairingId,
            Code = pairing.Code,
            ExpiresAtUtc = pairing.ExpiresAtUtc
        });
    }

    private static async Task<IResult> HandleApprovePairing(
        [FromBody] DisplayPairingApprovalDto request,
        IDisplayPairingService pairings,
        IDisplayService displayService,
        IPlaybackSessionRegistry registry,
        IDisplayConnectionTracker displayConnections,
        ICredentialResolver credentialResolver,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        (UserSession? owner, IResult? rejection) = await ResolveOwnerAsync(
            credentialResolver,
            context,
            cancellationToken);
        if (owner is null)
        {
            return rejection!;
        }

        // Validated before the code is claimed, so a typo in the name does not burn the code.
        if (!TryNormalizeName(request.Name, out string name, out IResult? invalidName))
        {
            return invalidName!;
        }

        PendingDisplayPairing? pairing = string.IsNullOrWhiteSpace(request.Code) ? null : pairings.Claim(request.Code);
        if (pairing is null || !registry.IsConnected(pairing.ConnectionId))
        {
            return Results.NotFound(new { Message = "Pairing code not found, already used, or expired." });
        }

        CreatedDisplay created = await displayService.CreateAsync(owner.Id, name, cancellationToken);

        await hubContext.Clients.Client(pairing.ConnectionId).ReceiveDisplayPaired(new DisplayPairedDto
        {
            PairingId = pairing.PairingId,
            DisplayId = created.Display.Id,
            Name = created.Display.Name,
            Token = created.Token
        });

        // A send to a closed connection is dropped silently and only the token's hash is stored,
        // so a Display whose screen left mid-approval could never be used. Remove it instead.
        ILogger logger = loggerFactory.CreateLogger(LOGGER_CATEGORY);
        if (!registry.IsConnected(pairing.ConnectionId))
        {
            await displayService.RevokeAsync(owner.Id, created.Display.Id, CancellationToken.None);
            await displayConnections.DisconnectAsync(created.Display.Id);
            logger.LogWarning(
                "Removed Display {DisplayId}: connection {ConnectionId} closed before its token was delivered",
                created.Display.Id,
                pairing.ConnectionId);
            return Results.NotFound(new
            {
                Message = "The waiting screen disconnected before pairing finished. Request a new code."
            });
        }

        logger.LogInformation(
            "Display {DisplayId} paired to connection {ConnectionId}",
            created.Display.Id,
            pairing.ConnectionId);

        await NotifyDisplaysChangedAsync(hubContext, owner.Id);
        return Results.Created($"/api/displays/{created.Display.Id}", created.Display.ToDto());
    }

    private static async Task<IResult> HandleList(
        IDisplayService displayService,
        ICredentialResolver credentialResolver,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (UserSession? owner, IResult? rejection) = await ResolveOwnerAsync(
            credentialResolver,
            context,
            cancellationToken);
        if (owner is null)
        {
            return rejection!;
        }

        IReadOnlyList<DisplayInfo> displays = await displayService.ListAsync(owner.Id, cancellationToken);
        return Results.Ok(displays.Select(display => display.ToDto()).ToList());
    }

    private static async Task<IResult> HandleRename(
        string displayId,
        [FromBody] DisplayRenameDto request,
        IDisplayService displayService,
        ICredentialResolver credentialResolver,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (UserSession? owner, IResult? rejection) = await ResolveOwnerAsync(
            credentialResolver,
            context,
            cancellationToken);
        if (owner is null)
        {
            return rejection!;
        }

        if (!TryNormalizeName(request.Name, out string name, out IResult? invalidName))
        {
            return invalidName!;
        }

        bool renamed = await displayService.RenameAsync(owner.Id, displayId, name, cancellationToken);
        if (!renamed)
        {
            return DisplayNotFound();
        }

        await NotifyDisplaysChangedAsync(hubContext, owner.Id);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleRevoke(
        string displayId,
        IDisplayService displayService,
        IDisplayConnectionTracker displayConnections,
        ICredentialResolver credentialResolver,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (UserSession? owner, IResult? rejection) = await ResolveOwnerAsync(
            credentialResolver,
            context,
            cancellationToken);
        if (owner is null)
        {
            return rejection!;
        }

        bool revoked = await displayService.RevokeAsync(owner.Id, displayId, cancellationToken);
        if (!revoked)
        {
            return DisplayNotFound();
        }

        await displayConnections.DisconnectAsync(displayId);
        await NotifyDisplaysChangedAsync(hubContext, owner.Id);
        return Results.NoContent();
    }

    /// <summary>Lets every Session connection of the owner refresh its Displays list.</summary>
    private static Task NotifyDisplaysChangedAsync(
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        string ownerSessionId) =>
        hubContext.Clients.Group(PlaybackGroups.Owner(ownerSessionId)).ReceiveDisplaysChanged();

    /// <summary>
    /// Owner-only: 401 without a credential, 403 for a Display token, which is view-only.
    /// </summary>
    private static async Task<(UserSession? Owner, IResult? Rejection)> ResolveOwnerAsync(
        ICredentialResolver credentialResolver,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is null)
        {
            return (null, Results.Unauthorized());
        }

        if (!caller.IsSession)
        {
            return (null, Results.StatusCode(StatusCodes.Status403Forbidden));
        }

        return (caller.Owner, null);
    }

    private static bool TryNormalizeName(string? name, out string normalized, out IResult? rejection)
    {
        normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > DisplayInfo.MAX_NAME_LENGTH)
        {
            rejection = Results.BadRequest(new
            {
                Message = $"Name is required and must be at most {DisplayInfo.MAX_NAME_LENGTH} characters."
            });
            return false;
        }

        rejection = null;
        return true;
    }

    private static IResult DisplayNotFound() => Results.NotFound(new { Message = "Display not found." });
}
