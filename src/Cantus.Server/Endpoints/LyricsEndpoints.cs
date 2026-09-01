using System.Threading;
using System.Threading.Tasks;
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

namespace Cantus.Server.Endpoints;

public static class LyricsEndpoints
{
    public static IEndpointRouteBuilder MapLyricsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/lyrics").WithTags("Lyrics & Playback");

        group.MapGet("/{trackId}", HandleGetCachedLyrics)
            .WithName("GetCachedLyrics")
            .WithSummary("Retrieves cached lyrics for a specific track ID.");

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

    private static async Task<IResult> HandleSetTrackOffset(
        [FromBody] TrackOffsetDto request,
        ILyricsCacheRepository cacheRepository,
        IPlaybackSessionRegistry registry,
        ISessionTokenResolver sessionResolver,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TrackId))
        {
            return Results.BadRequest(new { Message = "TrackId is required." });
        }

        await cacheRepository.SetTrackOffsetAsync(request.TrackId, request.OffsetMs, cancellationToken);

        string? sessionId = sessionResolver.ResolveSessionId(context);
        if (!string.IsNullOrEmpty(sessionId))
        {
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

            await hubContext.Clients.Group($"user_{sessionId}").ReceiveTrackOffset(request);
        }
        else
        {
            UserPlaybackSnapshot? activeSnapshot = registry.GetActivePlaybackSnapshot();
            if (activeSnapshot?.PlaybackState?.CurrentTrack?.Id == request.TrackId)
            {
                registry.UpdateUserState(
                    activeSnapshot.UserId,
                    activeSnapshot.DisplayName,
                    activeSnapshot.PlaybackState,
                    activeSnapshot.Lyrics,
                    request.OffsetMs);

                await hubContext.Clients.Group($"user_{activeSnapshot.UserId}").ReceiveTrackOffset(request);
            }
            else
            {
                await hubContext.Clients.All.ReceiveTrackOffset(request);
            }
        }

        return Results.Ok(request);
    }
}
