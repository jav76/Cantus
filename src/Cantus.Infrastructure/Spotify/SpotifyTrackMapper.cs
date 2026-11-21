using Cantus.Core.Models;
using SpotifyAPI.Web;

namespace Cantus.Infrastructure.Spotify;

internal static class SpotifyTrackMapper
{
    private const string ARTIST_SEPARATOR = ", ";

    public static TrackInfo ToTrackInfo(FullTrack track)
    {
        return new TrackInfo
        {
            Id = track.Id ?? string.Empty,
            Title = track.Name,
            Artist = string.Join(ARTIST_SEPARATOR, track.Artists.Select(a => a.Name)),
            Album = track.Album?.Name,
            AlbumArtUrl = track.Album?.Images?.FirstOrDefault()?.Url,
            Duration = TimeSpan.FromMilliseconds(track.DurationMs),
            IsExplicit = track.Explicit
        };
    }
}
