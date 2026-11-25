using Cantus.Core.Models;
using Cantus.Core.Search;

namespace Cantus.Infrastructure.CoverArt;

/// <summary>
/// Decides which release groups may supply a track's cover. A recording counts only when its
/// title core equals or contains the track's (or the reverse), a credited artist matches on
/// whole words, and its length is within <see cref="DURATION_TOLERANCE_SECONDS"/>. All three
/// are required, because a wrong cover is worse than none. Of that recording's releases, one
/// counts when its title matches the track's album, or when it is an official studio album,
/// EP or single; compilations, live albums and bootlegs are used only on an album match.
/// </summary>
public static class MusicBrainzReleaseMatcher
{
    public const int DURATION_TOLERANCE_SECONDS = 5;

    private const string STATUS_OFFICIAL = "Official";
    private const int ALBUM_EXACT = 2;
    private const int ALBUM_CONTAINED = 1;
    private const int ALBUM_NO_MATCH = 0;
    private const int UNLISTED_TYPE_RANK = 3;

    private static readonly string[] StudioTypesByPreference = ["Album", "EP", "Single"];

    /// <summary>
    /// Up to <paramref name="maxGroups"/> release group ids, best first: an exact album match,
    /// then a contained one, then an exact recording title, then album before EP before single,
    /// then the closest length, then the earliest release.
    /// </summary>
    public static IReadOnlyList<Guid> SelectReleaseGroups(
        IReadOnlyList<MusicBrainzRecording> recordings,
        TrackInfo track,
        int maxGroups)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(track);

        string titleCore = LyricTextNormalizer.TitleCore(track.Title);
        if (titleCore.Length == 0 || track.Duration <= TimeSpan.Zero)
        {
            return [];
        }

        IReadOnlyList<string> trackArtists = LyricTextNormalizer.ArtistKeys(track.Artist);
        string albumCore = LyricTextNormalizer.TitleCore(track.Album);
        double toleranceMs = TimeSpan.FromSeconds(DURATION_TOLERANCE_SECONDS).TotalMilliseconds;
        List<Candidate> candidates = new();

        foreach (MusicBrainzRecording recording in recordings)
        {
            if (recording.Length is not { } length)
            {
                continue;
            }

            double distanceMs = Math.Abs((length - track.Duration).TotalMilliseconds);
            string recordingCore = LyricTextNormalizer.TitleCore(recording.Title);
            bool exactTitle = string.Equals(recordingCore, titleCore, StringComparison.Ordinal);
            if (distanceMs > toleranceMs
                || (!exactTitle && !TrackNameMatch.EitherContainsWords(recordingCore, titleCore))
                || !TrackNameMatch.AnyArtistMatches(trackArtists, ArtistKeys(recording.Artists)))
            {
                continue;
            }

            foreach (MusicBrainzRelease release in recording.Releases)
            {
                int albumMatch = MatchAlbum(albumCore, release.Title);
                int typeRank = StudioTypeRank(release);
                if (albumMatch == ALBUM_NO_MATCH && typeRank == UNLISTED_TYPE_RANK)
                {
                    continue;
                }

                candidates.Add(new Candidate(release.ReleaseGroupId, albumMatch, exactTitle, typeRank, distanceMs, release.Date));
            }
        }

        return candidates
            .OrderByDescending(c => c.AlbumMatch)
            .ThenByDescending(c => c.ExactTitle)
            .ThenBy(c => c.TypeRank)
            .ThenBy(c => c.DistanceMs)
            .ThenBy(c => string.IsNullOrEmpty(c.Date))
            .ThenBy(c => c.Date, StringComparer.Ordinal)
            .Select(c => c.ReleaseGroupId)
            .Distinct()
            .Take(maxGroups)
            .ToList();
    }

    private static int MatchAlbum(string albumCore, string releaseTitle)
    {
        if (albumCore.Length == 0)
        {
            return ALBUM_NO_MATCH;
        }

        string releaseCore = LyricTextNormalizer.TitleCore(releaseTitle);
        if (string.Equals(releaseCore, albumCore, StringComparison.Ordinal))
        {
            return ALBUM_EXACT;
        }

        return TrackNameMatch.EitherContainsWords(releaseCore, albumCore) ? ALBUM_CONTAINED : ALBUM_NO_MATCH;
    }

    /// <summary>The release's place in <see cref="StudioTypesByPreference"/>, or the unlisted rank.</summary>
    private static int StudioTypeRank(MusicBrainzRelease release)
    {
        if (!string.Equals(release.Status, STATUS_OFFICIAL, StringComparison.OrdinalIgnoreCase)
            || release.SecondaryTypes.Count > 0)
        {
            return UNLISTED_TYPE_RANK;
        }

        int rank = Array.FindIndex(
            StudioTypesByPreference,
            type => string.Equals(type, release.PrimaryType, StringComparison.OrdinalIgnoreCase));
        return rank < 0 ? UNLISTED_TYPE_RANK : rank;
    }

    private static List<string> ArtistKeys(IReadOnlyList<string> artists)
    {
        List<string> keys = new();
        foreach (string artist in artists)
        {
            foreach (string key in LyricTextNormalizer.ArtistKeys(artist))
            {
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private readonly record struct Candidate(
        Guid ReleaseGroupId,
        int AlbumMatch,
        bool ExactTitle,
        int TypeRank,
        double DistanceMs,
        string? Date);
}
