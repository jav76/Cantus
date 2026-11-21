using System.Globalization;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Spotify;

/// <summary>
/// Finds the Spotify track for a guess that came from a lyrics provider, so it
/// can be played. At most two searches: a field-filtered one on the title core
/// and primary artist, then one plain-text retry. Nothing is returned unless
/// <see cref="SpotifyTrackMatcher"/> accepts a hit.
/// </summary>
public sealed class SpotifyTrackResolver
{
    public const int SEARCH_LIMIT = 5;

    private readonly ISpotifyCatalogClient _catalogClient;
    private readonly ILogger<SpotifyTrackResolver> _logger;

    public SpotifyTrackResolver(ISpotifyCatalogClient catalogClient, ILogger<SpotifyTrackResolver> logger)
    {
        _catalogClient = catalogClient;
        _logger = logger;
    }

    /// <returns>The accepted track, or null when Spotify has no matching track.</returns>
    /// <exception cref="SpotifyRateLimitedException">Spotify calls are paused.</exception>
    public async Task<TrackInfo?> ResolveAsync(
        string accessToken,
        string title,
        string artist,
        int? durationMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        foreach (string query in BuildQueries(title, artist))
        {
            IReadOnlyList<TrackInfo> tracks = await _catalogClient.SearchTracksAsync(
                accessToken,
                query,
                SEARCH_LIMIT,
                cancellationToken);

            TrackInfo? match = SpotifyTrackMatcher.SelectBest(tracks, title, artist, durationMs);
            if (match is not null)
            {
                return match;
            }

            _logger.LogDebug("No accepted Spotify match among {HitCount} search hits", tracks.Count);
        }

        return null;
    }

    internal static IReadOnlyList<string> BuildQueries(string title, string artist)
    {
        List<string> queries = new();

        string titleCore = LyricTextNormalizer.TitleCore(title);
        string primaryArtist = LyricTextNormalizer.ArtistKey(artist);
        if (titleCore.Length > 0)
        {
            queries.Add(primaryArtist.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, "track:\"{0}\" artist:\"{1}\"", titleCore, primaryArtist)
                : string.Format(CultureInfo.InvariantCulture, "track:\"{0}\"", titleCore));
        }

        string plain = string.IsNullOrWhiteSpace(artist) ? title.Trim() : $"{title.Trim()} {artist.Trim()}";
        if (!queries.Contains(plain, StringComparer.Ordinal))
        {
            queries.Add(plain);
        }

        return queries;
    }
}
