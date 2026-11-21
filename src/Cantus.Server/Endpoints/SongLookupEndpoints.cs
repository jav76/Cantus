using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cantus.Server.Endpoints;

/// <summary>
/// Owner-only song lookup by remembered lyrics, and playing or queueing a guess. A Display
/// token is view-only and gets 403 before any lyrics are read or any provider is called.
/// </summary>
public static class SongLookupEndpoints
{
    public static IEndpointRouteBuilder MapSongLookupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/lookup").WithTags("Song Lookup");

        group.MapPost("/search", HandleSearch)
            .RequireRateLimiting(SongLookupServiceCollectionExtensions.SEARCH_RATE_LIMIT_POLICY)
            .WithName("SearchSongByLyrics")
            .WithSummary("Ranks songs that match a remembered lyric fragment, from the library first, then online.");

        group.MapPost("/play", HandlePlay)
            .RequireRateLimiting(SongLookupServiceCollectionExtensions.PLAY_RATE_LIMIT_POLICY)
            .WithName("PlaySongLookupGuess")
            .WithSummary("Plays or queues a song lookup guess on the owner's active Spotify device.");

        return endpoints;
    }

    private static async Task<IResult> HandleSearch(
        [FromBody] SongLookupSearchRequestDto request,
        ISongLookupService lookupService,
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

        if (!TryParseScope(request.Scope, out SongLookupScope scope))
        {
            return Results.BadRequest(new { Message = $"Unknown scope '{request.Scope}'." });
        }

        LyricFragment fragment = LyricTextNormalizer.PrepareFragment(request.Fragment);
        if (!fragment.IsValid)
        {
            return Results.BadRequest(new { Message = DescribeInvalidFragment(fragment.Status) });
        }

        SongLookupResult result;
        try
        {
            result = await lookupService.LookupAsync(
                request.Fragment!,
                scope,
                owner.SpotifyUserId,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }

        return Results.Ok(result.ToDto());
    }

    private static async Task<IResult> HandlePlay(
        [FromBody] SongLookupPlayRequestDto request,
        ISongPlaybackService playbackService,
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

        SongPlayAction action;
        switch (request.Action)
        {
            case SongLookupPlayRequestDto.ACTION_PLAY:
                action = SongPlayAction.Play;
                break;
            case SongLookupPlayRequestDto.ACTION_QUEUE:
                action = SongPlayAction.Queue;
                break;
            default:
                return Results.BadRequest(new { Message = "Action must be 'play' or 'queue'." });
        }

        bool byId = !string.IsNullOrEmpty(request.SpotifyTrackId);
        if (byId && !SongPlaybackService.IsValidTrackId(request.SpotifyTrackId))
        {
            return Results.BadRequest(new { Message = "SpotifyTrackId must be a 22-character Spotify track id." });
        }

        if (!byId && (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist)))
        {
            return Results.BadRequest(new { Message = "SpotifyTrackId, or Title and Artist, are required." });
        }

        SongPlayRequest playRequest = new(
            action,
            byId ? request.SpotifyTrackId : null,
            request.Title,
            request.Artist,
            request.DurationMs);

        SongPlayResult result;
        try
        {
            result = await playbackService.PlayAsync(
                owner.Id,
                owner.AccessToken,
                playRequest,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }

        return Results.Ok(result.ToDto());
    }

    private static bool TryParseScope(string? scope, out SongLookupScope parsed)
    {
        switch (scope)
        {
            case null:
            case SongLookupSearchRequestDto.SCOPE_AUTO:
                parsed = SongLookupScope.Auto;
                return true;
            case SongLookupSearchRequestDto.SCOPE_LOCAL:
                parsed = SongLookupScope.Local;
                return true;
            case SongLookupSearchRequestDto.SCOPE_ONLINE:
                parsed = SongLookupScope.Online;
                return true;
            default:
                parsed = SongLookupScope.Auto;
                return false;
        }
    }

    private static string DescribeInvalidFragment(LyricFragmentStatus status)
    {
        return status switch
        {
            LyricFragmentStatus.TooLong =>
                $"Fragment must be at most {LyricMatchConstants.MAX_FRAGMENT_CHARACTERS} characters.",
            LyricFragmentStatus.TooShort =>
                $"Fragment is too short: type at least {LyricMatchConstants.MIN_WORD_TOKENS} words "
                + $"or {LyricMatchConstants.MIN_CJK_TOKENS} CJK characters.",
            _ => "Fragment is required."
        };
    }

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
}
