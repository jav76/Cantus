using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Lyrics;
using Cantus.Server.Services;

namespace Cantus.Server.Models;

public static class DtoMappingExtensions
{
    private const int CONFIDENCE_DECIMALS = 2;

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
            ActiveUserDisplayName = activeUserName,
            Source = state.Source == PlaybackSource.Local
                ? PlaybackStateDto.SOURCE_LOCAL
                : PlaybackStateDto.SOURCE_SPOTIFY,
            Capabilities = state.Capabilities?.ToDto()
        };
    }

    public static PlaybackCapabilitiesDto ToDto(this PlaybackCapabilities capabilities)
    {
        return new PlaybackCapabilitiesDto
        {
            CanPlay = capabilities.CanPlay,
            CanPause = capabilities.CanPause,
            CanGoNext = capabilities.CanGoNext,
            CanGoPrevious = capabilities.CanGoPrevious,
            CanSeek = capabilities.CanSeek
        };
    }

    public static PlaybackCapabilities ToModel(this PlaybackCapabilitiesDto? capabilities)
    {
        if (capabilities is null)
        {
            return PlaybackCapabilities.None;
        }

        return new PlaybackCapabilities
        {
            CanPlay = capabilities.CanPlay,
            CanPause = capabilities.CanPause,
            CanGoNext = capabilities.CanGoNext,
            CanGoPrevious = capabilities.CanGoPrevious,
            CanSeek = capabilities.CanSeek
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
            Kind = session.Kind == SessionKind.Local
                ? AuthorizedSessionDto.KIND_LOCAL
                : AuthorizedSessionDto.KIND_SPOTIFY,
            SpotifyUserId = session.SpotifyUserId,
            DisplayName = session.DisplayName,
            Email = session.Email,
            ProfileImageUrl = session.ProfileImageUrl,
            IsCurrentlyPlaying = isCurrentlyPlaying,
            CreatedAtUtc = session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc
        };
    }

    public static SongLookupResponseDto ToDto(this SongLookupResult result)
    {
        return new SongLookupResponseDto
        {
            Outcome = result.Outcome switch
            {
                SongMatchOutcome.Accepted => "accepted",
                SongMatchOutcome.Ambiguous => "ambiguous",
                _ => "none"
            },
            OnlineRecommended = result.OnlineRecommended,
            Guesses = result.Guesses.Select(guess => guess.ToDto()).ToList(),
            Providers = result.Providers.Select(provider => provider.ToDto()).ToList()
        };
    }

    public static SongGuessDto ToDto(this SongGuess guess)
    {
        return new SongGuessDto
        {
            Source = guess.Source,
            SpotifyTrackId = guess.SpotifyTrackId,
            Title = guess.Title,
            Artist = guess.Artist,
            Album = guess.Album,
            DurationMs = guess.DurationMs,
            Confidence = Math.Round(guess.Confidence, CONFIDENCE_DECIMALS, MidpointRounding.AwayFromZero),
            Verified = guess.Verified,
            MatchedLines = guess.MatchedLines,
            VersionCount = guess.VersionCount
        };
    }

    public static SongLookupProviderDto ToDto(this SongLookupProviderStatus status)
    {
        return new SongLookupProviderDto
        {
            Name = status.ProviderName,
            Status = status.State switch
            {
                SongLookupProviderState.Ok => "ok",
                SongLookupProviderState.Skipped => "skipped",
                SongLookupProviderState.Disabled => "disabled",
                SongLookupProviderState.RateLimited => "rateLimited",
                _ => "unavailable"
            },
            RetryAfterSeconds = ToRetryAfterSeconds(status.RetryAfter)
        };
    }

    public static SongLookupPlayResponseDto ToDto(this SongPlayResult result)
    {
        return new SongLookupPlayResponseDto
        {
            Status = result.Status switch
            {
                SongPlayStatus.Success => "success",
                SongPlayStatus.NotOnSpotify => "notOnSpotify",
                SongPlayStatus.NoActiveDevice => "noActiveDevice",
                SongPlayStatus.MissingPermissions => "missingPermissions",
                SongPlayStatus.RateLimited => "rateLimited",
                _ => "failed"
            },
            Track = result.Track?.ToDto(),
            RetryAfterSeconds = ToRetryAfterSeconds(result.RetryAfter)
        };
    }

    /// <summary>Whole seconds, rounded up so a client never retries a moment too early.</summary>
    private static int? ToRetryAfterSeconds(TimeSpan? retryAfter)
    {
        return retryAfter is null ? null : (int)Math.Ceiling(Math.Max(0, retryAfter.Value.TotalSeconds));
    }
}
