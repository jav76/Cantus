using Cantus.Core.Models;
using Cantus.Core.Search;

namespace Cantus.Infrastructure.Spotify;

/// <summary>
/// Decides whether a Spotify search hit is the song a lookup guessed. A hit is
/// accepted only when its title core equals or contains the guess's (or the
/// reverse), and either an artist matches on whole words or the duration is
/// within <see cref="DURATION_TOLERANCE_SECONDS"/>. The duration branch covers
/// artist names written in another script.
/// </summary>
public static class SpotifyTrackMatcher
{
    public const int DURATION_TOLERANCE_SECONDS = 3;

    private const char WORD_BOUNDARY = ' ';

    /// <summary>
    /// The best accepted hit, or null. Exact title cores beat contained ones, an
    /// artist match beats a duration-only match, then the closest duration wins,
    /// then Spotify's own order.
    /// </summary>
    public static TrackInfo? SelectBest(
        IReadOnlyList<TrackInfo> tracks,
        string title,
        string artist,
        int? durationMs)
    {
        string guessCore = LyricTextNormalizer.TitleCore(title);
        if (guessCore.Length == 0)
        {
            return null;
        }

        IReadOnlyList<string> guessArtists = LyricTextNormalizer.ArtistKeys(artist);
        TrackInfo? best = null;
        (bool ExactTitle, bool ArtistMatch, double DurationDistanceMs) bestRank = default;

        foreach (TrackInfo track in tracks)
        {
            string trackCore = LyricTextNormalizer.TitleCore(track.Title);
            bool exactTitle = string.Equals(trackCore, guessCore, StringComparison.Ordinal);
            if (!exactTitle && !ContainsWords(trackCore, guessCore) && !ContainsWords(guessCore, trackCore))
            {
                continue;
            }

            bool artistMatch = ArtistsMatch(guessArtists, LyricTextNormalizer.ArtistKeys(track.Artist));
            double distanceMs = durationMs is int expectedMs
                ? Math.Abs(track.Duration.TotalMilliseconds - expectedMs)
                : double.MaxValue;
            bool durationMatch = distanceMs <= TimeSpan.FromSeconds(DURATION_TOLERANCE_SECONDS).TotalMilliseconds;
            if (!artistMatch && !durationMatch)
            {
                continue;
            }

            (bool, bool, double) rank = (exactTitle, artistMatch, distanceMs);
            if (best is null || IsBetter(rank, bestRank))
            {
                best = track;
                bestRank = rank;
            }
        }

        return best;
    }

    private static bool IsBetter(
        (bool ExactTitle, bool ArtistMatch, double DurationDistanceMs) candidate,
        (bool ExactTitle, bool ArtistMatch, double DurationDistanceMs) current)
    {
        if (candidate.ExactTitle != current.ExactTitle)
        {
            return candidate.ExactTitle;
        }

        if (candidate.ArtistMatch != current.ArtistMatch)
        {
            return candidate.ArtistMatch;
        }

        return candidate.DurationDistanceMs < current.DurationDistanceMs;
    }

    private static bool ArtistsMatch(IReadOnlyList<string> guessArtists, IReadOnlyList<string> trackArtists)
    {
        foreach (string guessArtist in guessArtists)
        {
            foreach (string trackArtist in trackArtists)
            {
                if (ContainsWords(trackArtist, guessArtist) || ContainsWords(guessArtist, trackArtist))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsWords(string haystack, string needle)
    {
        if (needle.Length == 0 || haystack.Length == 0)
        {
            return false;
        }

        return (WORD_BOUNDARY + haystack + WORD_BOUNDARY).Contains(
            WORD_BOUNDARY + needle + WORD_BOUNDARY,
            StringComparison.Ordinal);
    }
}
