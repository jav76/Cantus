using Cantus.Core.Models;

namespace Cantus.Server.Models;

public static class DtoMappingExtensions
{
    public static TrackInfoDto ToDto(this TrackInfo track)
    {
        return new TrackInfoDto
        {
            Id = track.Id,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtUrl = track.AlbumArtUrl,
            DurationMs = (long)track.Duration.TotalMilliseconds,
            IsExplicit = track.IsExplicit
        };
    }

    public static PlaybackStateDto ToDto(
        this PlaybackState state,
        string? activeUserId = null,
        string? activeUserName = null)
    {
        return new PlaybackStateDto
        {
            CurrentTrack = state.CurrentTrack?.ToDto(),
            ProgressMs = (long)state.Progress.TotalMilliseconds,
            IsPlaying = state.IsPlaying,
            TimestampUtc = state.TimestampUtc,
            DeviceName = state.DeviceName,
            VolumePercent = state.VolumePercent,
            ActiveUserId = activeUserId,
            ActiveUserDisplayName = activeUserName
        };
    }

    public static LyricsDto ToDto(this SyncedLyrics lyrics)
    {
        List<LyricLineDto> lines = lyrics.Lines.Select(l => new LyricLineDto
        {
            TimestampMs = (long)l.Timestamp.TotalMilliseconds,
            Text = l.Text
        }).ToList();

        return new LyricsDto
        {
            TrackId = lyrics.TrackId,
            Title = lyrics.Title,
            Artist = lyrics.Artist,
            Album = lyrics.Album,
            IsSynced = lyrics.IsSynced,
            IsInstrumental = lyrics.IsInstrumental,
            Lines = lines,
            PlainLyrics = lyrics.PlainLyrics
        };
    }

    public static AuthorizedSessionDto ToDto(this UserSession session, bool isCurrentlyPlaying = false)
    {
        return new AuthorizedSessionDto
        {
            Id = session.Id,
            SpotifyUserId = session.SpotifyUserId,
            DisplayName = session.DisplayName,
            Email = session.Email,
            ProfileImageUrl = session.ProfileImageUrl,
            IsCurrentlyPlaying = isCurrentlyPlaying,
            CreatedAtUtc = session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc
        };
    }
}
