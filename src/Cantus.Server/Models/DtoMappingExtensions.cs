using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;

namespace Cantus.Server.Models;

public static class DtoMappingExtensions
{
    public static DisplayDto ToDto(this DisplayInfo display)
    {
        return new DisplayDto
        {
            Id = display.Id,
            Name = display.Name,
            CreatedAtUtc = display.CreatedAtUtc,
            LastSeenAtUtc = display.LastSeenAtUtc
        };
    }

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
            IsShuffled = state.IsShuffled,
            RepeatMode = state.RepeatMode,
            ActiveUserId = activeUserId,
            ActiveUserDisplayName = activeUserName
        };
    }

    public static LyricsCandidatesDto ToDto(
        this LyricsCandidateList list,
        string trackId,
        LyricsOverride? activeOverride = null)
    {
        return new LyricsCandidatesDto
        {
            TrackId = trackId,
            Candidates = list.Candidates.Select(c => c.ToDto()).ToList(),
            UnavailableProviders = list.UnavailableProviders,
            ActiveOverride = activeOverride?.ToDto()
        };
    }

    public static LyricsActiveOverrideDto ToDto(this LyricsOverride lyricsOverride)
    {
        return new LyricsActiveOverrideDto
        {
            Kind = lyricsOverride.Kind switch
            {
                LyricsOverrideKind.NoLyrics => LyricsActiveOverrideDto.KIND_NO_LYRICS,
                _ => LyricsActiveOverrideDto.KIND_CANDIDATE
            },
            Provider = lyricsOverride.Provider,
            CandidateId = lyricsOverride.ProviderCandidateId
        };
    }

    public static LyricsCandidateDto ToDto(this LyricsCandidate candidate)
    {
        return new LyricsCandidateDto
        {
            Provider = candidate.Provider,
            ProviderCandidateId = candidate.ProviderCandidateId,
            Title = candidate.Title,
            Artist = candidate.Artist,
            Album = candidate.Album,
            DurationMs = (long)candidate.Duration.TotalMilliseconds,
            IsSynced = candidate.IsSynced,
            IsInstrumental = candidate.IsInstrumental,
            Preview = candidate.Preview
        };
    }

    public static LyricsDto ToDto(this SyncedLyrics lyrics)
    {
        List<LyricLineDto> lines = lyrics.Lines.Select(l => new LyricLineDto
        {
            TimestampMs = (long)l.Timestamp.TotalMilliseconds,
            Text = l.Text,
            Syllables = l.Syllables is { Count: > 0 }
                ? l.Syllables
                    .Select(s => new LyricSyllableDto
                    {
                        TimestampMs = (long)s.Timestamp.TotalMilliseconds,
                        DurationMs = (long)s.Duration.TotalMilliseconds,
                        Text = s.Text
                    })
                    .ToList()
                : null
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

    /// <summary>
    /// The lyrics to send for a track, or an empty payload when it has none so
    /// clients clear the previous track's lines.
    /// </summary>
    public static LyricsDto ToLyricsDto(this TrackInfo track, SyncedLyrics? lyrics)
    {
        return lyrics?.ToDto() ?? new LyricsDto
        {
            TrackId = track.Id,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            Lines = [],
            IsSynced = false,
            IsInstrumental = false,
            PlainLyrics = null
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
