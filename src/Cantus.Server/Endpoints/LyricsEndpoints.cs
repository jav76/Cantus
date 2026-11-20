using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;

namespace Cantus.Server.Endpoints;

public static class LyricsEndpoints
{
    public static IEndpointRouteBuilder MapLyricsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/lyrics").WithTags("Lyrics & Playback");

        group.MapGet("/{trackId}", HandleGetCachedLyrics)
            .WithName("GetCachedLyrics")
            .WithSummary("Retrieves cached lyrics for a specific track ID.");

        group.MapGet("/{trackId}/candidates", HandleGetLyricsCandidates)
            .WithName("GetLyricsCandidates")
            .WithSummary("Lists lyrics candidates from every enabled provider for the owner's current track.");

        group.MapPost("/offset", HandleSetTrackOffset)
            .WithName("SetTrackOffset")
            .WithSummary("Saves a manual timing offset adjustment for a track.");

        return endpoints;
    }

    private static async Task<IResult> HandleGetCachedLyrics(
        string trackId,
        ILyricsCacheRepository cacheRepository,
        CancellationToken cancellationToken)
    {
        SyncedLyrics? lyrics = await cacheRepository.GetCachedLyricsAsync(trackId, cancellationToken);
        if (lyrics is null)
        {
            return Results.NotFound(new { Message = $"Lyrics for track '{trackId}' not found in cache." });
        }

        return Results.Ok(lyrics.ToDto());
    }

    private static async Task<IResult> HandleGetLyricsCandidates(
        string trackId,
        LyricsCandidateService candidateService,
        ICredentialResolver credentialResolver,
        IPlaybackSessionRegistry registry,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // Candidates feed the owner's lyrics correction, which changes what
        // every viewer of the track sees, so only a signed-in Session may list
        // them. Each call also fans out to external providers.
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is null)
        {
            return Results.Unauthorized();
        }

        if (!caller.IsSession)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Providers search by title, artist and duration, which only the
        // owner's live playback state carries for a given track id.
        TrackInfo? currentTrack = registry.GetUserState(caller.Owner.Id)?.PlaybackState?.CurrentTrack;
        if (currentTrack is null || !string.Equals(currentTrack.Id, trackId, StringComparison.Ordinal))
        {
            return Results.NotFound(new { Message = $"Track '{trackId}' is not currently playing for this session." });
        }

        LyricsCandidateList candidates = await candidateService.GetCandidatesAsync(currentTrack, cancellationToken);
        return Results.Ok(candidates.ToDto(trackId));
    }

    private static async Task<IResult> HandleSetTrackOffset(
        [FromBody] TrackOffsetDto request,
        ILyricsCacheRepository cacheRepository,
        IPlaybackSessionRegistry registry,
        ICredentialResolver credentialResolver,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // Saving an offset mutates shared cache state and is broadcast to the
        // owning session, so it requires an authenticated caller. The previous
        // fallback accepted anonymous writes and broadcast them to every
        // connected client.
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is null)
        {
            return Results.Unauthorized();
        }

        if (!caller.IsSession)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        string sessionId = caller.Owner.Id;

        if (string.IsNullOrWhiteSpace(request.TrackId))
        {
            return Results.BadRequest(new { Message = "TrackId is required." });
        }

        await cacheRepository.SetTrackOffsetAsync(request.TrackId, request.OffsetMs, cancellationToken);

        UserPlaybackSnapshot? userSnapshot = registry.GetUserState(sessionId);
        if (userSnapshot?.PlaybackState?.CurrentTrack?.Id == request.TrackId)
        {
            registry.UpdateUserState(
                userSnapshot.UserId,
                userSnapshot.DisplayName,
                userSnapshot.PlaybackState,
                userSnapshot.Lyrics,
                request.OffsetMs);
        }

        await hubContext.Clients.Viewers(sessionId).ReceiveTrackOffset(request);

        return Results.Ok(request);
    }
}
