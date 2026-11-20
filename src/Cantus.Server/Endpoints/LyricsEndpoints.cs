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
    private const string PROVIDERS_UNAVAILABLE_MESSAGE =
        "The lyrics providers could not be reached. The current lyrics are unchanged.";

    public static IEndpointRouteBuilder MapLyricsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/lyrics").WithTags("Lyrics & Playback");

        group.MapGet("/{trackId}", HandleGetCachedLyrics)
            .WithName("GetCachedLyrics")
            .WithSummary("Retrieves cached lyrics for a specific track ID.");

        group.MapGet("/{trackId}/candidates", HandleGetLyricsCandidates)
            .WithName("GetLyricsCandidates")
            .WithSummary("Lists lyrics candidates from every enabled provider for the owner's current track.");

        group.MapPost("/{trackId}/override", HandleSetLyricsOverride)
            .WithName("SetLyricsOverride")
            .WithSummary("Applies, re-fetches or clears the owner's lyrics override for their current track.");

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
        LyricsOverride? lyricsOverride = await cacheRepository.GetLyricsOverrideAsync(trackId, cancellationToken);
        SyncedLyrics? lyrics = lyricsOverride is not null
            ? lyricsOverride.Lyrics
            : await cacheRepository.GetCachedLyricsAsync(trackId, cancellationToken);
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
        (IResult? rejection, TrackInfo? currentTrack) = await ResolveOwnersCurrentTrackAsync(
            trackId,
            credentialResolver,
            registry,
            context,
            cancellationToken);
        if (currentTrack is null)
        {
            return rejection!;
        }

        LyricsCandidateList candidates = await candidateService.GetCandidatesAsync(currentTrack, cancellationToken);
        return Results.Ok(candidates.ToDto(trackId));
    }

    private static async Task<IResult> HandleSetLyricsOverride(
        string trackId,
        [FromBody] LyricsOverrideRequestDto request,
        LyricsOverrideService overrideService,
        ICredentialResolver credentialResolver,
        IPlaybackSessionRegistry registry,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // An override applies server-wide, so only a signed-in Session may set
        // one, and only for the track it is playing. A Display is view-only.
        (IResult? rejection, TrackInfo? currentTrack) = await ResolveOwnersCurrentTrackAsync(
            trackId,
            credentialResolver,
            registry,
            context,
            cancellationToken);
        if (currentTrack is null)
        {
            return rejection!;
        }

        SyncedLyrics? lyrics;
        switch (request.Action)
        {
            case LyricsOverrideRequestDto.ACTION_CANDIDATE:
                if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.CandidateId))
                {
                    return Results.BadRequest(new { Message = "Provider and CandidateId are required." });
                }

                LyricsOverrideOutcome outcome = await overrideService.UseCandidateAsync(
                    currentTrack,
                    request.Provider,
                    request.CandidateId,
                    cancellationToken);
                if (outcome.Status != LyricsOverrideStatus.Applied)
                {
                    return RejectCandidate(outcome.Status, request);
                }

                lyrics = outcome.Lyrics;
                break;
            case LyricsOverrideRequestDto.ACTION_NO_LYRICS:
                await overrideService.MarkNoLyricsAsync(currentTrack, cancellationToken);
                lyrics = null;
                break;
            case LyricsOverrideRequestDto.ACTION_REFETCH:
                LyricsOverrideOutcome refetched = await overrideService.RefetchAsync(currentTrack, cancellationToken);
                if (refetched.Status != LyricsOverrideStatus.Applied)
                {
                    return Results.Json(
                        new { Message = PROVIDERS_UNAVAILABLE_MESSAGE },
                        statusCode: StatusCodes.Status502BadGateway);
                }

                lyrics = refetched.Lyrics;
                break;
            case LyricsOverrideRequestDto.ACTION_CLEAR:
                lyrics = await overrideService.ClearAsync(currentTrack, cancellationToken);
                break;
            default:
                return Results.BadRequest(new { Message = $"Unknown override action '{request.Action}'." });
        }

        LyricsDto lyricsDto = currentTrack.ToLyricsDto(lyrics);
        await BroadcastLyricsAsync(trackId, lyrics, lyricsDto, registry, hubContext);
        return Results.Ok(lyricsDto);
    }

    private static IResult RejectCandidate(LyricsOverrideStatus status, LyricsOverrideRequestDto request)
    {
        return status switch
        {
            LyricsOverrideStatus.UnknownProvider => Results.BadRequest(
                new { Message = $"Lyrics provider '{request.Provider}' is not enabled." }),
            LyricsOverrideStatus.CandidateNotFound => Results.NotFound(
                new { Message = $"{request.Provider} has no lyrics for candidate '{request.CandidateId}'." }),
            _ => Results.Json(
                new { Message = $"{request.Provider} could not be reached. The current lyrics are unchanged." },
                statusCode: StatusCodes.Status502BadGateway)
        };
    }

    /// <summary>
    /// Overrides are server-wide, so every Session currently on the track, and
    /// its Displays, gets the new lyrics, not only the owner who changed them.
    /// The snapshot is updated too, because the poller only re-resolves lyrics
    /// on a track change and clients that connect later read the snapshot.
    /// </summary>
    private static async Task BroadcastLyricsAsync(
        string trackId,
        SyncedLyrics? lyrics,
        LyricsDto lyricsDto,
        IPlaybackSessionRegistry registry,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext)
    {
        IReadOnlyList<string> userIds = registry.ReplaceLyricsForTrack(trackId, lyrics);
        foreach (string userId in userIds)
        {
            await hubContext.Clients.Viewers(userId).ReceiveLyrics(lyricsDto);
        }
    }

    /// <returns>
    /// The caller's current track when it is <paramref name="trackId"/>;
    /// otherwise null and the response to send instead. Providers search by
    /// title, artist and duration, which only the owner's live playback state
    /// carries for a given track id.
    /// </returns>
    private static async Task<(IResult? Rejection, TrackInfo? CurrentTrack)> ResolveOwnersCurrentTrackAsync(
        string trackId,
        ICredentialResolver credentialResolver,
        IPlaybackSessionRegistry registry,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        CallerCredential? caller = await credentialResolver.ResolveAsync(context, cancellationToken);
        if (caller is null)
        {
            return (Results.Unauthorized(), null);
        }

        if (!caller.IsSession)
        {
            return (Results.StatusCode(StatusCodes.Status403Forbidden), null);
        }

        TrackInfo? currentTrack = registry.GetUserState(caller.Owner.Id)?.PlaybackState?.CurrentTrack;
        if (currentTrack is null || !string.Equals(currentTrack.Id, trackId, StringComparison.Ordinal))
        {
            return (
                Results.NotFound(new { Message = $"Track '{trackId}' is not currently playing for this session." }),
                null);
        }

        return (null, currentTrack);
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
