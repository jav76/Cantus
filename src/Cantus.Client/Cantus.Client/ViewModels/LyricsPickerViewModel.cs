using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// The "Wrong lyrics?" picker for the owner's current track: lists the
/// candidates every provider has and applies the owner's choice (a candidate,
/// a re-fetch, "no lyrics", or clearing the override). The server broadcasts
/// the new lyrics to every viewer; <see cref="LyricsChanged"/> also hands them
/// to the root so this client updates even if it misses the broadcast.
/// </summary>
public sealed class LyricsPickerViewModel : INotifyPropertyChanged
{
    private const string EMPTY_TEXT = "No other lyrics were found for this track.";
    private const string NO_PROVIDER_TEXT = "No lyrics provider could be reached. Try again later.";
    private const string SERVER_UNREACHABLE_TEXT = "Could not reach the Cantus server. Try again.";
    private const string SERVER_OUTDATED_TEXT = "This Cantus server is too old to change lyrics. Update the server.";
    private const string TRACK_GONE_TEXT = "This track is no longer playing.";
    private const string CHOICE_GONE_TEXT = "Those lyrics could not be loaded. The track may have changed.";
    private const string PROVIDER_UNAVAILABLE_TEXT =
        "The lyrics provider could not be reached. Your lyrics are unchanged.";
    private const string CHOICE_REJECTED_TEXT = "Those lyrics can no longer be used. Pick another.";
    private const string REQUEST_REJECTED_TEXT = "The Cantus server turned down this request.";
    private const string SIGNED_OUT_TEXT = "Sign in again to change lyrics.";
    private const string NOT_ALLOWED_TEXT = "Only the account owner can change lyrics.";
    private const string NO_LYRICS_OVERRIDE_TEXT = "You marked this track as having no lyrics.";
    private const string TRACK_SEPARATOR = " · ";

    private readonly ILyricsCorrectionService _service;
    private readonly SessionViewModel _session;
    private readonly Func<TrackInfoPayload?> _currentTrack;
    private readonly DisplayPairingViewModel? _pairing;

    private string? _trackId;
    private bool _isAvailable;
    private bool _isOpen;
    private bool _isLoading;
    private bool _isApplying;
    private string _trackText = string.Empty;
    private string _statusText = string.Empty;
    private string _unavailableText = string.Empty;
    private LyricsActiveOverridePayload? _activeOverride;

    // Bumped whenever the picker closes or the track changes, so a request
    // that answers afterwards is dropped instead of showing stale results.
    private int _generation;

    public LyricsPickerViewModel(
        ILyricsCorrectionService service,
        SessionViewModel session,
        Func<TrackInfoPayload?> currentTrack,
        DisplayPairingViewModel? pairing = null)
    {
        _service = service;
        _session = session;
        _currentTrack = currentTrack;
        _pairing = pairing;
        _trackId = CurrentTrackId();
        _isAvailable = ComputeIsAvailable();
        _session.PropertyChanged += OnSessionPropertyChanged;
        if (_pairing is not null)
        {
            _pairing.PropertyChanged += OnPairingPropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised with the lyrics now in effect after a choice is applied.</summary>
    public event Action<LyricsPayload>? LyricsChanged;

    public ObservableCollection<LyricsCandidateItem> Candidates { get; } = new();

    /// <summary>
    /// Whether the owner can correct the current track's lyrics: a Session is
    /// signed in, a track is playing, and this client is not a paired Display.
    /// A Display normally never gets the Session list, but one paired after a
    /// sign-in can still hold the stale list, so the Display check is kept too.
    /// </summary>
    public bool IsAvailable => _isAvailable;

    public Visibility ActionVisibility => _isAvailable ? Visibility.Visible : Visibility.Collapsed;

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (_isOpen != value)
            {
                _isOpen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanChoose));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(CanChoose));
            }
        }
    }

    public Visibility LoadingVisibility => _isLoading ? Visibility.Visible : Visibility.Collapsed;

    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (_isApplying != value)
            {
                _isApplying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanChoose));
            }
        }
    }

    public bool CanChoose => _isOpen && !_isLoading && !_isApplying;

    /// <summary>The track the open picker lists candidates for, as "Title · Artist".</summary>
    public string TrackText
    {
        get => _trackText;
        private set
        {
            if (_trackText != value)
            {
                _trackText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Why the list is empty or a choice failed; empty when there is nothing to say.</summary>
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

    public Visibility StatusVisibility =>
        string.IsNullOrEmpty(_statusText) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Names the providers that could not be reached, whose candidates are missing.</summary>
    public string UnavailableText
    {
        get => _unavailableText;
        private set
        {
            if (_unavailableText != value)
            {
                _unavailableText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UnavailableVisibility));
            }
        }
    }

    public Visibility UnavailableVisibility =>
        string.IsNullOrEmpty(_unavailableText) ? Visibility.Collapsed : Visibility.Visible;

    public bool HasActiveOverride => _activeOverride is not null;

    public string ActiveOverrideText => _activeOverride switch
    {
        null => string.Empty,
        { Kind: LyricsActiveOverridePayload.KIND_NO_LYRICS } => NO_LYRICS_OVERRIDE_TEXT,
        _ => $"You picked these lyrics from {_activeOverride.Provider}."
    };

    public Visibility ClearOverrideVisibility => HasActiveOverride ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Opens the picker for the current track and loads its candidates. Reloads when already open.</summary>
    public async Task OpenAsync()
    {
        NotifyTrackChanged();
        TrackInfoPayload? track = _currentTrack();
        string? trackId = _trackId;
        if (!_isAvailable || track is null || trackId is null)
        {
            return;
        }

        int generation = ++_generation;
        TrackText = string.Join(TRACK_SEPARATOR, track.Title, track.Artist);
        Candidates.Clear();
        SetActiveOverride(null);
        StatusText = string.Empty;
        UnavailableText = string.Empty;
        IsApplying = false;
        IsOpen = true;
        IsLoading = true;

        LyricsCandidatesResult result = await _service.GetCandidatesAsync(trackId);
        if (generation != _generation)
        {
            return;
        }

        IsLoading = false;
        if (result.Status != LyricsCorrectionStatus.Succeeded || result.Candidates is null)
        {
            StatusText = DescribeLoadFailure(result.Status);
            return;
        }

        ShowCandidates(result.Candidates);
    }

    public void Close()
    {
        _generation++;
        IsLoading = false;
        IsApplying = false;
        IsOpen = false;
    }

    public Task UseCandidateAsync(LyricsCandidateItem candidate)
        => ApplyAsync(LyricsOverrideRequestPayload.ForCandidate(candidate.Provider, candidate.CandidateId));

    /// <summary>Asks the server to match the track again, dropping any override.</summary>
    public Task RefetchAsync() => ApplyAsync(LyricsOverrideRequestPayload.Refetch());

    public Task MarkNoLyricsAsync() => ApplyAsync(LyricsOverrideRequestPayload.NoLyrics());

    /// <summary>Returns the track to automatic matching.</summary>
    public Task ClearOverrideAsync() => ApplyAsync(LyricsOverrideRequestPayload.Clear());

    /// <summary>
    /// Called when the current track may have changed. A different track
    /// closes the picker, since its candidates belong to the previous one.
    /// </summary>
    public void NotifyTrackChanged()
    {
        string? trackId = CurrentTrackId();
        if (trackId != _trackId)
        {
            _trackId = trackId;
            if (_isOpen)
            {
                Close();
            }
        }

        UpdateAvailability();
    }

    private async Task ApplyAsync(LyricsOverrideRequestPayload request)
    {
        string? trackId = _trackId;
        if (!CanChoose || trackId is null)
        {
            return;
        }

        int generation = _generation;
        StatusText = string.Empty;
        IsApplying = true;

        LyricsOverrideResult result = await _service.ApplyOverrideAsync(trackId, request);
        if (generation != _generation)
        {
            return;
        }

        IsApplying = false;
        if (result.Status != LyricsCorrectionStatus.Succeeded)
        {
            StatusText = DescribeChoiceFailure(result.Status);
            return;
        }

        Close();
        if (result.Lyrics is not null)
        {
            LyricsChanged?.Invoke(result.Lyrics);
        }
    }

    private void ShowCandidates(LyricsCandidatesPayload payload)
    {
        LyricsActiveOverridePayload? activeOverride = payload.ActiveOverride;
        SetActiveOverride(activeOverride);

        foreach (LyricsCandidatePayload candidate in payload.Candidates)
        {
            bool isActive = activeOverride is { Kind: LyricsActiveOverridePayload.KIND_CANDIDATE }
                && activeOverride.Provider == candidate.Provider
                && activeOverride.CandidateId == candidate.ProviderCandidateId;
            Candidates.Add(new LyricsCandidateItem(candidate, isActive));
        }

        List<string> unavailable = payload.UnavailableProviders ?? new();
        UnavailableText = unavailable.Count == 0
            ? string.Empty
            : $"{JoinNames(unavailable)} could not be reached, so some lyrics may be missing.";

        if (Candidates.Count == 0)
        {
            StatusText = unavailable.Count > 0 ? NO_PROVIDER_TEXT : EMPTY_TEXT;
        }
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };

    private void SetActiveOverride(LyricsActiveOverridePayload? activeOverride)
    {
        _activeOverride = activeOverride;
        OnPropertyChanged(nameof(HasActiveOverride));
        OnPropertyChanged(nameof(ActiveOverrideText));
        OnPropertyChanged(nameof(ClearOverrideVisibility));
    }

    private static string DescribeLoadFailure(LyricsCorrectionStatus status) => status switch
    {
        LyricsCorrectionStatus.NotFound => TRACK_GONE_TEXT,
        _ => DescribeCommonFailure(status)
    };

    private static string DescribeChoiceFailure(LyricsCorrectionStatus status) => status switch
    {
        LyricsCorrectionStatus.NotFound => CHOICE_GONE_TEXT,
        LyricsCorrectionStatus.Rejected => CHOICE_REJECTED_TEXT,
        _ => DescribeCommonFailure(status)
    };

    private static string DescribeCommonFailure(LyricsCorrectionStatus status) => status switch
    {
        LyricsCorrectionStatus.SignedOut => SIGNED_OUT_TEXT,
        LyricsCorrectionStatus.NotAllowed => NOT_ALLOWED_TEXT,
        LyricsCorrectionStatus.ProviderUnavailable => PROVIDER_UNAVAILABLE_TEXT,
        LyricsCorrectionStatus.Rejected => REQUEST_REJECTED_TEXT,
        LyricsCorrectionStatus.ServerOutdated => SERVER_OUTDATED_TEXT,
        _ => SERVER_UNREACHABLE_TEXT
    };

    private string? CurrentTrackId()
    {
        string? trackId = _currentTrack()?.Id;
        return string.IsNullOrEmpty(trackId) ? null : trackId;
    }

    private bool ComputeIsAvailable() =>
        _session.CurrentUserSession is not null
        && _trackId is not null
        && _pairing?.IsDisplay != true;

    private void UpdateAvailability()
    {
        bool isAvailable = ComputeIsAvailable();
        if (isAvailable == _isAvailable)
        {
            return;
        }

        _isAvailable = isAvailable;
        if (!isAvailable && _isOpen)
        {
            Close();
        }

        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(ActionVisibility));
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.CurrentUserSession))
        {
            UpdateAvailability();
        }
    }

    private void OnPairingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplayPairingViewModel.IsDisplay))
        {
            UpdateAvailability();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
