using Cantus.Core.Interfaces;
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
        var group = endpoints.MapGroup("/api/lyrics").WithTags("Lyrics & Playback");

        group.MapGet("/{trackId}", async (
            string trackId,
            ILyricsCacheRepository cacheRepository,
            CancellationToken cancellationToken) =>
        {
            var lyrics = await cacheRepository.GetCachedLyricsAsync(trackId, cancellationToken);
            if (lyrics is null)
            {
                return Results.NotFound(new { Message = $"Lyrics for track '{trackId}' not found in cache." });
            }

            return Results.Ok(lyrics.ToDto());
        })
        .WithName("GetCachedLyrics")
        .WithSummary("Retrieves cached lyrics for a specific track ID.");

        group.MapPost("/offset", async (
            [FromBody] TrackOffsetDto request,
            ILyricsCacheRepository cacheRepository,
            IPlaybackSessionRegistry registry,
            IHubContext<PlaybackHub, IPlaybackClient> hubContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TrackId))
            {
                return Results.BadRequest(new { Message = "TrackId is required." });
            }

            await cacheRepository.SetTrackOffsetAsync(request.TrackId, request.OffsetMs, cancellationToken);

            var activeSnapshot = registry.GetActivePlaybackSnapshot();
            if (activeSnapshot?.PlaybackState?.CurrentTrack?.Id == request.TrackId)
            {
                registry.UpdateUserState(
                    activeSnapshot.UserId,
                    activeSnapshot.DisplayName,
                    activeSnapshot.PlaybackState,
                    activeSnapshot.Lyrics,
                    request.OffsetMs);
            }

            await hubContext.Clients.All.ReceiveTrackOffset(request);

            return Results.Ok(request);
        })
        .WithName("SetTrackOffset")
        .WithSummary("Saves a manual timing offset adjustment for a track.");

        return endpoints;
    }
}
