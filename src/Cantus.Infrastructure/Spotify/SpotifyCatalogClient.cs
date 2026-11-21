using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace Cantus.Infrastructure.Spotify;

/// <summary>
/// Track search against the Spotify Web API. It shares the 429 back-off with the
/// playback poller: no request is sent while the gate is limited, and a 429 here
/// pauses every Spotify caller.
/// </summary>
public sealed class SpotifyCatalogClient : ISpotifyCatalogClient
{
    private const string MARKET_FROM_TOKEN = "from_token";

    private readonly ILogger<SpotifyCatalogClient> _logger;
    private readonly SpotifyRateLimitGate _rateLimitGate;
    private readonly Func<string, SpotifyClient> _clientFactory;

    public SpotifyCatalogClient(
        ILogger<SpotifyCatalogClient> logger,
        SpotifyRateLimitGate rateLimitGate)
        : this(logger, accessToken => new SpotifyClient(accessToken), rateLimitGate)
    {
    }

    /// <summary>Test seam: lets tests route SDK calls through a fake HTTP handler.</summary>
    internal SpotifyCatalogClient(
        ILogger<SpotifyCatalogClient> logger,
        Func<string, SpotifyClient> clientFactory,
        SpotifyRateLimitGate rateLimitGate)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _rateLimitGate = rateLimitGate;
    }

    public async Task<IReadOnlyList<TrackInfo>> SearchTracksAsync(
        string accessToken,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return Array.Empty<TrackInfo>();
        }

        if (_rateLimitGate.IsLimited)
        {
            throw new SpotifyRateLimitedException(_rateLimitGate.Remaining);
        }

        try
        {
            SpotifyClient spotify = _clientFactory(accessToken);
            SearchRequest request = new(SearchRequest.Types.Track, query)
            {
                Limit = limit,
                Market = MARKET_FROM_TOKEN
            };
            SearchResponse response = await spotify.Search.Item(request, cancellationToken);

            IList<FullTrack>? items = response.Tracks?.Items;
            if (items is null)
            {
                return Array.Empty<TrackInfo>();
            }

            return items
                .Where(track => track is not null && !string.IsNullOrEmpty(track.Id))
                .Select(SpotifyTrackMapper.ToTrackInfo)
                .ToList();
        }
        catch (APITooManyRequestsException ex)
        {
            TimeSpan applied = _rateLimitGate.Report(ex.RetryAfter);
            _logger.LogWarning(
                "Spotify rate limit hit on track search. Pausing Spotify calls until {RateLimitUntil} UTC (Retry after {RetryAfter})",
                _rateLimitGate.LimitedUntilUtc,
                applied);
            throw new SpotifyRateLimitedException(_rateLimitGate.Remaining, ex);
        }
    }
}
