using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Find a song by remembered lyrics. A search first asks the library only, so its guesses
/// show at once; when the server recommends it, a second call searches online while those
/// guesses stay visible. A newer search, <see cref="Cancel"/> or <see cref="Reset"/> drops
/// any response still in flight.
/// </summary>
public sealed class SongLookupViewModel : INotifyPropertyChanged
{
    internal const string SERVER_UNREACHABLE_TEXT = "Couldn't reach the Cantus server. Try again.";
    internal const string INBOUND_RATE_LIMITED_TEXT = "Too many searches. Try again in a minute.";
    internal const string FORBIDDEN_TEXT = "Finding a song needs a signed-in account.";
    internal const string UNAUTHORIZED_TEXT = "Sign in with Spotify to find songs.";
    internal const string POSSIBLE_MATCHES_TEXT = "Possible matches";
    internal const string AMBIGUOUS_TEXT = "Several songs could match";
    internal const string ACCEPTED_TEXT = "Best match";
    internal const string NO_MATCH_TEXT = "No match found. Try another line you remember.";
    internal const string SEARCHING_LIBRARY_TEXT = "Searching your library...";
    internal const string SEARCHING_ONLINE_TEXT = "Searching online...";

    private readonly ISongLookupClient _client;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    private CancellationTokenSource? _searchCancellation;
    private int _generation;
    private string _fragment = string.Empty;
    private SongLookupState _state = SongLookupState.Idle;
    private string _statusText = string.Empty;
    private string _resultsStatusText = string.Empty;
    private bool _canSearchOnline;

    public SongLookupViewModel(ISongLookupClient client)
    {
        _client = client;

        try
        {
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        }
        catch
        {
            _dispatcherQueue = null;
        }

        Guesses.CollectionChanged += (_, _) => OnPropertyChanged(nameof(GuessesVisibility));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SongGuessViewModel> Guesses { get; } = new();

    public string Fragment
    {
        get => _fragment;
        set
        {
            string next = value ?? string.Empty;
            if (_fragment != next)
            {
                _fragment = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSearch));
            }
        }
    }

    public bool CanSearch => !string.IsNullOrWhiteSpace(_fragment);

    public SongLookupState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSearching));
                OnPropertyChanged(nameof(SearchingVisibility));
                OnPropertyChanged(nameof(SearchOnlineVisibility));
            }
        }
    }

    public bool IsSearching => _state is SongLookupState.SearchingLibrary or SongLookupState.SearchingOnline;

    public Visibility SearchingVisibility => IsSearching ? Visibility.Visible : Visibility.Collapsed;

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

    public Visibility GuessesVisibility => Guesses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The "Not it? Search online" link, offered when no online provider was asked yet.</summary>
    public Visibility SearchOnlineVisibility =>
        _canSearchOnline && _state is SongLookupState.Results or SongLookupState.Empty
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Searches the library, then online when the server recommends it.</summary>
    public async Task SearchAsync()
    {
        if (!CanSearch)
        {
            return;
        }

        string fragment = _fragment.Trim();
        (int generation, CancellationToken token) = BeginSearch();
        ClearGuesses();
        _canSearchOnline = false;
        SetState(SongLookupState.SearchingLibrary, SEARCHING_LIBRARY_TEXT);

        SongLookupCallResult? local = await CallAsync(fragment, SongLookupSearchRequestPayload.SCOPE_LOCAL, token);
        if (local is null || generation != _generation)
        {
            return;
        }

        bool searchOnline = local.Status == SongLookupCallStatus.Success && local.Response!.OnlineRecommended;
        RunOnUIThread(() =>
        {
            if (generation != _generation)
            {
                return;
            }

            if (searchOnline)
            {
                ShowGuesses(local.Response!);
                _resultsStatusText = DescribeResults(local.Response!);
                SetState(SongLookupState.SearchingOnline, SEARCHING_ONLINE_TEXT);
            }
            else
            {
                ApplyResult(local, keepGuessesOnFailure: false);
            }
        });

        if (!searchOnline || generation != _generation)
        {
            return;
        }

        await SearchOnlineCoreAsync(fragment, SongLookupSearchRequestPayload.SCOPE_AUTO, generation, token);
    }

    /// <summary>"Not it? Search online": asks the online providers even when the library had an answer.</summary>
    public async Task SearchOnlineAsync()
    {
        if (!CanSearch)
        {
            return;
        }

        string fragment = _fragment.Trim();
        (int generation, CancellationToken token) = BeginSearch();
        if (Guesses.Count == 0)
        {
            _resultsStatusText = string.Empty;
        }

        SetState(SongLookupState.SearchingOnline, SEARCHING_ONLINE_TEXT);

        await SearchOnlineCoreAsync(fragment, SongLookupSearchRequestPayload.SCOPE_ONLINE, generation, token);
    }

    /// <summary>Drops the search in flight. Guesses already shown stay.</summary>
    public void Cancel()
    {
        bool wasSearching = IsSearching;
        InvalidateSearch();
        if (!wasSearching)
        {
            return;
        }

        if (Guesses.Count > 0)
        {
            SetState(SongLookupState.Results, _resultsStatusText);
        }
        else
        {
            SetState(SongLookupState.Idle, string.Empty);
        }
    }

    /// <summary>Drops the search in flight and clears the fragment and every guess.</summary>
    public void Reset()
    {
        InvalidateSearch();
        ClearGuesses();
        Fragment = string.Empty;
        _resultsStatusText = string.Empty;
        SetState(SongLookupState.Idle, string.Empty);
    }

    internal static string FormatRetryHint(int? retryAfterSeconds)
    {
        return retryAfterSeconds switch
        {
            null or <= 0 => "Try again in a minute.",
            1 => "Try again in 1 second.",
            int seconds => $"Try again in {seconds} seconds."
        };
    }

    private async Task SearchOnlineCoreAsync(string fragment, string scope, int generation, CancellationToken token)
    {
        SongLookupCallResult? online = await CallAsync(fragment, scope, token);
        if (online is null || generation != _generation)
        {
            return;
        }

        RunOnUIThread(() =>
        {
            if (generation == _generation)
            {
                ApplyResult(online, keepGuessesOnFailure: true);
            }
        });
    }

    /// <returns>Null when the search was cancelled.</returns>
    private async Task<SongLookupCallResult?> CallAsync(string fragment, string scope, CancellationToken token)
    {
        try
        {
            return await _client.SearchAsync(fragment, scope, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            return new SongLookupCallResult(SongLookupCallStatus.Failed);
        }
    }

    private void ApplyResult(SongLookupCallResult result, bool keepGuessesOnFailure)
    {
        if (result.Status == SongLookupCallStatus.Success && result.Response is not null)
        {
            ApplyResponse(result.Response);
            return;
        }

        string failure = DescribeFailure(result);
        if (keepGuessesOnFailure && Guesses.Count > 0)
        {
            _canSearchOnline = true;
            SetState(SongLookupState.Results, $"{_resultsStatusText}. Online search failed: {failure}");
            return;
        }

        ClearGuesses();
        _canSearchOnline = false;
        SongLookupState state = result.Status == SongLookupCallStatus.RateLimited
            ? SongLookupState.RateLimited
            : SongLookupState.Error;
        SetState(state, failure);
    }

    private void ApplyResponse(SongLookupResponsePayload response)
    {
        ShowGuesses(response);
        _canSearchOnline = response.Providers.Any(p => p.Status == SongLookupProviderPayload.STATUS_SKIPPED)
            && response.Providers.All(p => p.Status != SongLookupProviderPayload.STATUS_OK);

        SongLookupProviderPayload? limited = response.Providers
            .FirstOrDefault(p => p.Status == SongLookupProviderPayload.STATUS_RATE_LIMITED);
        string? retryHint = limited is null
            ? null
            : $"Online search is paused. {FormatRetryHint(limited.RetryAfterSeconds)}";

        if (response.Guesses.Count == 0)
        {
            _resultsStatusText = string.Empty;
            if (retryHint is not null)
            {
                SetState(SongLookupState.RateLimited, retryHint);
                return;
            }

            bool unreachable = response.Providers.Any(p => p.Status == SongLookupProviderPayload.STATUS_UNAVAILABLE);
            SetState(
                SongLookupState.Empty,
                unreachable ? $"{NO_MATCH_TEXT} Online search could not be reached." : NO_MATCH_TEXT);
            return;
        }

        _resultsStatusText = DescribeResults(response);
        SetState(
            SongLookupState.Results,
            retryHint is null ? _resultsStatusText : $"{_resultsStatusText}. {retryHint}");
    }

    private static string DescribeResults(SongLookupResponsePayload response)
    {
        return response.Outcome switch
        {
            SongLookupResponsePayload.OUTCOME_ACCEPTED => ACCEPTED_TEXT,
            SongLookupResponsePayload.OUTCOME_AMBIGUOUS => AMBIGUOUS_TEXT,
            _ => POSSIBLE_MATCHES_TEXT
        };
    }

    private static string DescribeFailure(SongLookupCallResult result)
    {
        return result.Status switch
        {
            SongLookupCallStatus.BadRequest => result.ErrorMessage ?? "The server could not read that search.",
            SongLookupCallStatus.Unauthorized => UNAUTHORIZED_TEXT,
            SongLookupCallStatus.Forbidden => FORBIDDEN_TEXT,
            SongLookupCallStatus.RateLimited => INBOUND_RATE_LIMITED_TEXT,
            _ => SERVER_UNREACHABLE_TEXT
        };
    }

    private void ShowGuesses(SongLookupResponsePayload response)
    {
        Guesses.Clear();
        foreach (SongGuessPayload guess in response.Guesses)
        {
            Guesses.Add(new SongGuessViewModel(guess, _client, RunOnUIThread));
        }
    }

    private void ClearGuesses()
    {
        if (Guesses.Count > 0)
        {
            Guesses.Clear();
        }
    }

    private (int Generation, CancellationToken Token) BeginSearch()
    {
        InvalidateSearch();
        _searchCancellation = new CancellationTokenSource();
        return (_generation, _searchCancellation.Token);
    }

    private void InvalidateSearch()
    {
        _generation++;
        if (_searchCancellation is not null)
        {
            _searchCancellation.Cancel();
            _searchCancellation.Dispose();
            _searchCancellation = null;
        }
    }

    private void SetState(SongLookupState state, string statusText)
    {
        State = state;
        StatusText = statusText;
        OnPropertyChanged(nameof(SearchOnlineVisibility));
    }

    private void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue is not null)
        {
            try
            {
                if (!_dispatcherQueue.HasThreadAccess)
                {
                    _dispatcherQueue.TryEnqueue(() => action());
                    return;
                }
            }
            catch
            {
            }
        }
        action();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
