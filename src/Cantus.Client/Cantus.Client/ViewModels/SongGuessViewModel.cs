using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// One guess card in the Find a song dialog: what matched, where it came from, and Play
/// and Queue with a per-card status line.
/// </summary>
public sealed class SongGuessViewModel : INotifyPropertyChanged
{
    private const double PERCENT_SCALE = 100.0;

    internal const string NOT_ON_SPOTIFY_TEXT = "Couldn't find this song on Spotify.";

    private readonly ISongLookupClient _client;
    private readonly Action<Action> _runOnUIThread;

    private bool _isBusy;
    private string _statusText = string.Empty;

    public SongGuessViewModel(SongGuessPayload guess, ISongLookupClient client, Action<Action>? runOnUIThread = null)
    {
        Guess = guess;
        _client = client;
        _runOnUIThread = runOnUIThread ?? (action => action());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SongGuessPayload Guess { get; }

    public string Title => Guess.Title;

    public string Artist => Guess.Artist;

    /// <summary>"Artist / Album", or just the artist when the album is unknown.</summary>
    public string ArtistAlbumText =>
        string.IsNullOrWhiteSpace(Guess.Album) ? Guess.Artist : $"{Guess.Artist} / {Guess.Album}";

    public string MatchedLinesText => string.Join("\n", Guess.MatchedLines);

    public Visibility MatchedLinesVisibility =>
        Guess.MatchedLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsLocal => string.Equals(Guess.Source, SongGuessPayload.SOURCE_LOCAL, StringComparison.Ordinal);

    /// <summary>"86% match, in your library", "72% match, found online (NetEase)", or unverified online hits.</summary>
    public string MatchText
    {
        get
        {
            if (IsLocal)
            {
                return $"{ConfidencePercent}% match, in your library";
            }

            return Guess.Verified
                ? $"{ConfidencePercent}% match, found online ({Guess.Source})"
                : $"found online ({Guess.Source}), not verified";
        }
    }

    public int ConfidencePercent => (int)Math.Round(Math.Clamp(Guess.Confidence, 0.0, 1.0) * PERCENT_SCALE);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAct));
            }
        }
    }

    public bool CanAct => !IsBusy;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public Visibility StatusVisibility => _statusText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Task PlayAsync() => SendAsync(SongLookupPlayRequestPayload.ACTION_PLAY);

    public Task QueueAsync() => SendAsync(SongLookupPlayRequestPayload.ACTION_QUEUE);

    private async Task SendAsync(string action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = action == SongLookupPlayRequestPayload.ACTION_PLAY ? "Starting..." : "Adding to queue...";

        bool byId = !string.IsNullOrEmpty(Guess.SpotifyTrackId);
        SongLookupPlayRequestPayload request = new()
        {
            Action = action,
            SpotifyTrackId = byId ? Guess.SpotifyTrackId : null,
            Title = byId ? null : Guess.Title,
            Artist = byId ? null : Guess.Artist,
            DurationMs = byId ? null : Guess.DurationMs,
        };

        SongPlayCallResult result;
        try
        {
            result = await _client.PlayAsync(request);
        }
        catch (Exception)
        {
            result = new SongPlayCallResult(SongLookupCallStatus.Failed);
        }

        _runOnUIThread(() =>
        {
            StatusText = DescribePlayResult(action, result);
            IsBusy = false;
        });
    }

    internal static string DescribePlayResult(string action, SongPlayCallResult result)
    {
        bool isPlay = action == SongLookupPlayRequestPayload.ACTION_PLAY;
        return result.Status switch
        {
            SongLookupCallStatus.Success when result.Response is not null =>
                DescribeSpotifyStatus(isPlay, result.Response),
            SongLookupCallStatus.BadRequest => result.ErrorMessage ?? "Couldn't play this song.",
            SongLookupCallStatus.Unauthorized => "Sign in with Spotify to play songs.",
            SongLookupCallStatus.Forbidden => "Playing songs needs a signed-in account.",
            SongLookupCallStatus.RateLimited => "Too many requests. Try again in a minute.",
            _ => SongLookupViewModel.SERVER_UNREACHABLE_TEXT
        };
    }

    private static string DescribeSpotifyStatus(bool isPlay, SongLookupPlayResponsePayload response)
    {
        return response.Status switch
        {
            SongLookupPlayResponsePayload.STATUS_SUCCESS => DescribeSuccess(isPlay, response.Track),
            SongLookupPlayResponsePayload.STATUS_NOT_ON_SPOTIFY => NOT_ON_SPOTIFY_TEXT,
            SongLookupPlayResponsePayload.STATUS_NO_ACTIVE_DEVICE => PlaybackTransport.NO_ACTIVE_DEVICE_TEXT,
            SongLookupPlayResponsePayload.STATUS_MISSING_PERMISSIONS => PlaybackTransport.MISSING_PERMISSIONS_TEXT,
            SongLookupPlayResponsePayload.STATUS_RATE_LIMITED =>
                $"Spotify is busy. {SongLookupViewModel.FormatRetryHint(response.RetryAfterSeconds)}",
            _ => PlaybackTransport.SPOTIFY_UNREACHABLE_TEXT
        };
    }

    private static string DescribeSuccess(bool isPlay, TrackInfoPayload? track)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.Title))
        {
            return isPlay ? "Playing now." : "Added to the queue.";
        }

        return isPlay
            ? $"Playing {track.Title} by {track.Artist}."
            : $"Queued {track.Title} by {track.Artist}.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
