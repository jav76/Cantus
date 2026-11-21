using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>Which owner action the Display prompt is asking about.</summary>
public enum DisplayPromptMode
{
    None,
    Approve,
    Rename,
    Revoke,
}

/// <summary>
/// The owner's Displays: the list shown in settings (name, last seen) with rename and revoke,
/// and the prompt that approves a Display's pairing code, typed in or read from the
/// <c>?pair=</c> link. The list is fetched on sign-in, after each owner action, and whenever
/// the server reports that the owner's Displays changed. Last-seen text is re-described once
/// a minute so it ages between fetches.
/// </summary>
public sealed class DisplaysViewModel : INotifyPropertyChanged
{
    public const int MAX_NAME_LENGTH = 64;
    public const int MAX_CODE_LENGTH = 32;

    public const string APPROVE_TITLE = "Approve a display";
    public const string APPROVE_MESSAGE =
        "Enter the code shown on the screen and give it a name. It will show your lyrics and playback, view only.";
    public const string RENAME_TITLE = "Rename display";
    public const string RENAME_MESSAGE = "Choose a name you will recognize in this list.";
    public const string REVOKE_TITLE = "Remove display";

    public const string SIGN_IN_HINT = "Sign in with Spotify on this device first, then approve the code.";
    public const string EMPTY_MESSAGE =
        "No displays yet. Choose Pair as display on a TV or other screen, then approve its code here.";

    public const string CODE_NOT_FOUND_MESSAGE = "That code is not valid or has expired. Check the code on the screen.";
    public const string INVALID_NAME_MESSAGE = "Enter a name of 1 to 64 characters.";
    public const string NOT_SIGNED_IN_MESSAGE = "Sign in with Spotify on this device to manage displays.";
    public const string FORBIDDEN_MESSAGE = "A paired display cannot manage displays.";
    public const string RATE_LIMITED_MESSAGE = "Too many attempts. Wait a minute and try again.";
    public const string FAILED_MESSAGE = "Could not reach the Cantus server. Try again.";
    public const string DISPLAY_GONE_MESSAGE = "That display was already removed.";
    public const string LIST_FAILED_MESSAGE = "Could not load your displays.";

    private const char CODE_GROUP_SEPARATOR = '-';
    private const char QUERY_START = '?';
    private const char FRAGMENT_START = '#';
    private const char PARAMETER_SEPARATOR = '&';
    private const char VALUE_SEPARATOR = '=';

    private static readonly TimeSpan LAST_SEEN_REFRESH_INTERVAL = TimeSpan.FromMinutes(1);

    private readonly IDisplayManagementApi _api;
    private readonly TimeProvider _timeProvider;
    private readonly Func<long> _serverClockOffsetMs;
    private readonly Action<Action> _runOnUiThread;
    private readonly PendingPairCodeStore? _pendingCodes;
    private ITimer? _lastSeenTimer;

    private bool _isSignedIn;
    private bool _isLoading;
    private bool _hasLoaded;
    private string? _listError;
    private string? _noticeMessage;
    private int _refreshGeneration;

    private DisplayPromptMode _promptMode = DisplayPromptMode.None;
    private string _promptCode = string.Empty;
    private string _promptName = string.Empty;
    private string? _promptError;
    private string? _promptDisplayId;
    private string _promptDisplayName = string.Empty;
    private bool _isBusy;
    private int _promptGeneration;

    public DisplaysViewModel(
        IDisplayManagementApi api,
        TimeProvider? timeProvider = null,
        Func<long>? serverClockOffsetMs = null,
        Action<Action>? runOnUiThread = null,
        PendingPairCodeStore? pendingCodes = null)
    {
        _api = api;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _serverClockOffsetMs = serverClockOffsetMs ?? (() => 0);
        _runOnUiThread = runOnUiThread ?? (action => action());
        _pendingCodes = pendingCodes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DisplayItemViewModel> Displays { get; } = new();

    /// <summary>Whether a Session is signed in on this device; Displays are managed only then.</summary>
    public bool IsSignedIn => _isSignedIn;

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
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }
    }

    public Visibility SectionVisibility => IsSignedIn ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility =>
        _hasLoaded && !IsLoading && _listError is null && Displays.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string ListError => _listError ?? string.Empty;

    public Visibility ListErrorVisibility => _listError is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The outcome of the last finished owner action, such as a Display being paired.</summary>
    public string NoticeMessage => _noticeMessage ?? string.Empty;

    public Visibility NoticeVisibility => _noticeMessage is null ? Visibility.Collapsed : Visibility.Visible;

    public DisplayPromptMode PromptMode => _promptMode;

    public bool IsPromptOpen => _promptMode != DisplayPromptMode.None;

    public Visibility PromptVisibility => IsPromptOpen ? Visibility.Visible : Visibility.Collapsed;

    public string PromptTitle => _promptMode switch
    {
        DisplayPromptMode.Approve => APPROVE_TITLE,
        DisplayPromptMode.Rename => RENAME_TITLE,
        DisplayPromptMode.Revoke => REVOKE_TITLE,
        _ => string.Empty,
    };

    public string PromptMessage => _promptMode switch
    {
        DisplayPromptMode.Approve => APPROVE_MESSAGE,
        DisplayPromptMode.Rename => RENAME_MESSAGE,
        DisplayPromptMode.Revoke =>
            $"Remove {_promptDisplayName}? It stops showing your lyrics right away and has to be paired again.",
        _ => string.Empty,
    };

    public string ConfirmText => _promptMode switch
    {
        DisplayPromptMode.Approve => "Approve",
        DisplayPromptMode.Rename => "Save",
        DisplayPromptMode.Revoke => "Remove",
        _ => string.Empty,
    };

    public Visibility CodeFieldVisibility =>
        _promptMode == DisplayPromptMode.Approve ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NameFieldVisibility =>
        _promptMode is DisplayPromptMode.Approve or DisplayPromptMode.Rename ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SignInHintVisibility =>
        _promptMode == DisplayPromptMode.Approve && !IsSignedIn ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The code as typed. It is normalized (case, spaces, hyphens) only when sent.</summary>
    public string PromptCode
    {
        get => _promptCode;
        set
        {
            string code = value ?? string.Empty;
            if (_promptCode != code)
            {
                _promptCode = code;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public string PromptName
    {
        get => _promptName;
        set
        {
            string name = value ?? string.Empty;
            if (_promptName != name)
            {
                _promptName = name;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public string PromptError => _promptError ?? string.Empty;

    public Visibility PromptErrorVisibility => _promptError is null ? Visibility.Collapsed : Visibility.Visible;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BusyVisibility));
                OnPropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public bool CanConfirm => !IsBusy && _promptMode switch
    {
        DisplayPromptMode.Approve => IsValidCode(NormalizeCode(_promptCode)) && IsValidName(_promptName),
        DisplayPromptMode.Rename => IsValidName(_promptName),
        DisplayPromptMode.Revoke => true,
        _ => false,
    };

    /// <summary>
    /// Takes the sign-in state from the Session. Signing out empties the list and closes a
    /// rename or remove prompt; an approval prompt stays so the owner can sign in again.
    /// Signing in reopens the approval prompt for a code kept through the sign-in.
    /// </summary>
    public void SetSignedIn(bool isSignedIn)
    {
        if (_isSignedIn == isSignedIn)
        {
            return;
        }

        _isSignedIn = isSignedIn;
        if (isSignedIn)
        {
            StartLastSeenTimer();
            RestorePendingCode();
        }
        else
        {
            StopLastSeenTimer();
            _refreshGeneration++;
            _hasLoaded = false;
            IsLoading = false;
            Displays.Clear();
            SetListError(null);
            SetNotice(null);
            if (_promptMode is DisplayPromptMode.Rename or DisplayPromptMode.Revoke)
            {
                ClosePrompt();
            }
        }

        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(SectionVisibility));
        OnPropertyChanged(nameof(SignInHintVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    /// <summary>Fetches the owner's Displays. Only the latest of overlapping refreshes is applied.</summary>
    public async Task RefreshAsync()
    {
        if (!IsSignedIn)
        {
            return;
        }

        int generation = ++_refreshGeneration;
        IsLoading = true;
        DisplayListResult result = await _api.ListAsync();
        if (generation != _refreshGeneration)
        {
            return;
        }

        IsLoading = false;
        _hasLoaded = true;
        switch (result.Status)
        {
            case DisplayManagementStatus.Succeeded:
                ApplyList(result.Displays ?? Array.Empty<DisplayInfoPayload>());
                SetListError(null);
                break;
            case DisplayManagementStatus.NotSignedIn:
            case DisplayManagementStatus.Forbidden:
                Displays.Clear();
                SetListError(MessageFor(result.Status));
                break;
            default:
                SetListError(LIST_FAILED_MESSAGE);
                break;
        }
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    /// <summary>Opens the approval prompt, with the code filled in when it is known.</summary>
    public void OpenApproval(string? code = null)
    {
        OpenPrompt(DisplayPromptMode.Approve, null, string.Empty);
        PromptCode = code ?? string.Empty;
        PromptName = string.Empty;
    }

    /// <summary>
    /// Opens the approval prompt for the code in a <c>?pair=</c> link (the page address or
    /// just its query string). Returns false, leaving the prompt closed, when there is none.
    /// </summary>
    public bool OpenApprovalFromLink(string? urlOrQuery)
    {
        string? code = ParsePairCode(urlOrQuery);
        if (code is null)
        {
            return false;
        }

        OpenApproval(code);
        if (!IsSignedIn)
        {
            _pendingCodes?.Save(code, _timeProvider.GetUtcNow());
        }
        return true;
    }

    /// <summary>
    /// Keeps the approval prompt's code through a sign-in that leaves the page, so the prompt
    /// reopens with it once this device is signed in.
    /// </summary>
    public void KeepCodeThroughSignIn()
    {
        string code = NormalizeCode(_promptCode);
        if (_promptMode == DisplayPromptMode.Approve && !IsSignedIn && IsValidCode(code))
        {
            _pendingCodes?.Save(code, _timeProvider.GetUtcNow());
        }
    }

    /// <summary>Describes each Display's last-seen time against the current clock.</summary>
    public void RefreshLastSeen()
    {
        DateTimeOffset now = ServerNow();
        foreach (DisplayItemViewModel display in Displays)
        {
            display.RefreshLastSeen(now);
        }
    }

    public void StartRename(DisplayItemViewModel display)
    {
        OpenPrompt(DisplayPromptMode.Rename, display.Id, display.Name);
        PromptCode = string.Empty;
        PromptName = display.Name;
    }

    public void StartRevoke(DisplayItemViewModel display)
    {
        OpenPrompt(DisplayPromptMode.Revoke, display.Id, display.Name);
        PromptCode = string.Empty;
        PromptName = string.Empty;
    }

    /// <summary>Closes the prompt. A request already sent still finishes and refreshes the list.</summary>
    public void CancelPrompt()
    {
        if (_promptMode == DisplayPromptMode.Approve)
        {
            _pendingCodes?.Clear();
        }
        ClosePrompt();
    }

    /// <summary>Sends the prompt's action. Returns true when it succeeded; errors stay in the prompt.</summary>
    public async Task<bool> ConfirmPromptAsync()
    {
        if (!CanConfirm)
        {
            return false;
        }

        int generation = _promptGeneration;
        DisplayPromptMode mode = _promptMode;
        string? displayId = _promptDisplayId;
        string displayName = _promptDisplayName;
        SetPromptError(null);
        IsBusy = true;
        try
        {
            return mode switch
            {
                DisplayPromptMode.Approve => await ApproveAsync(generation, NormalizeCode(_promptCode), _promptName.Trim()),
                DisplayPromptMode.Rename => await RenameAsync(generation, displayId!, _promptName.Trim()),
                DisplayPromptMode.Revoke => await RevokeAsync(generation, displayId!, displayName),
                _ => false,
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Reads the pairing code from the <c>pair</c> query parameter of a link or query string.
    /// Returns null when the parameter is missing or does not hold a usable code.
    /// </summary>
    public static string? ParsePairCode(string? urlOrQuery)
    {
        if (string.IsNullOrWhiteSpace(urlOrQuery))
        {
            return null;
        }

        string query = urlOrQuery.Trim();
        int fragmentStart = query.IndexOf(FRAGMENT_START);
        if (fragmentStart >= 0)
        {
            query = query[..fragmentStart];
        }

        int queryStart = query.IndexOf(QUERY_START);
        if (queryStart >= 0)
        {
            query = query[(queryStart + 1)..];
        }
        else if (query.IndexOf(VALUE_SEPARATOR) < 0)
        {
            return null;
        }

        foreach (string parameter in query.Split(PARAMETER_SEPARATOR, StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = parameter.IndexOf(VALUE_SEPARATOR);
            string key = separator >= 0 ? parameter[..separator] : parameter;
            if (Unescape(key) != DisplayPairingViewModel.PAIR_QUERY_PARAMETER)
            {
                continue;
            }

            string code = NormalizeCode(separator >= 0 ? Unescape(parameter[(separator + 1)..]) : string.Empty);
            return IsValidCode(code) ? code : null;
        }

        return null;
    }

    /// <summary>Drops spaces and hyphens and upper-cases the code, the way the server compares it.</summary>
    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        StringBuilder normalized = new(code.Length);
        foreach (char character in code)
        {
            if (char.IsWhiteSpace(character) || character == CODE_GROUP_SEPARATOR)
            {
                continue;
            }
            normalized.Append(char.ToUpperInvariant(character));
        }
        return normalized.ToString();
    }

    private static bool IsValidCode(string normalizedCode) =>
        normalizedCode.Length is > 0 and <= MAX_CODE_LENGTH && normalizedCode.All(char.IsAsciiLetterOrDigit);

    private static bool IsValidName(string name)
    {
        string trimmed = name.Trim();
        return trimmed.Length is > 0 and <= MAX_NAME_LENGTH;
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static string MessageFor(DisplayManagementStatus status) => status switch
    {
        DisplayManagementStatus.InvalidName => INVALID_NAME_MESSAGE,
        DisplayManagementStatus.NotFound => DISPLAY_GONE_MESSAGE,
        DisplayManagementStatus.NotSignedIn => NOT_SIGNED_IN_MESSAGE,
        DisplayManagementStatus.Forbidden => FORBIDDEN_MESSAGE,
        DisplayManagementStatus.RateLimited => RATE_LIMITED_MESSAGE,
        _ => FAILED_MESSAGE,
    };

    private async Task<bool> ApproveAsync(int generation, string code, string name)
    {
        DisplayApprovalResult result = await _api.ApproveAsync(code, name);
        if (result.Status == DisplayManagementStatus.Succeeded)
        {
            CloseIfCurrent(generation);
            DisplayInfoPayload? display = result.Display;
            SetNotice($"{display?.Name ?? name} is paired. It switches to your lyrics in a moment.");
            if (display is not null && IsSignedIn)
            {
                ApplyDisplay(display);
            }
            await RefreshAsync();
            return true;
        }

        string message = result.Status == DisplayManagementStatus.NotFound
            ? CODE_NOT_FOUND_MESSAGE
            : MessageFor(result.Status);
        ShowErrorIfCurrent(generation, message);
        return false;
    }

    private async Task<bool> RenameAsync(int generation, string displayId, string name)
    {
        DisplayManagementStatus status = await _api.RenameAsync(displayId, name);
        if (status == DisplayManagementStatus.Succeeded)
        {
            CloseIfCurrent(generation);
            DisplayItemViewModel? display = Find(displayId);
            if (display is not null)
            {
                display.Name = name;
            }
            await RefreshAsync();
            return true;
        }

        if (status == DisplayManagementStatus.NotFound)
        {
            CloseIfCurrent(generation);
            RemoveDisplay(displayId);
            SetNotice(DISPLAY_GONE_MESSAGE);
            await RefreshAsync();
            return false;
        }

        ShowErrorIfCurrent(generation, MessageFor(status));
        return false;
    }

    private async Task<bool> RevokeAsync(int generation, string displayId, string displayName)
    {
        DisplayManagementStatus status = await _api.RevokeAsync(displayId);
        if (status is DisplayManagementStatus.Succeeded or DisplayManagementStatus.NotFound)
        {
            CloseIfCurrent(generation);
            RemoveDisplay(displayId);
            SetNotice(status == DisplayManagementStatus.Succeeded ? $"{displayName} was removed." : DISPLAY_GONE_MESSAGE);
            await RefreshAsync();
            return status == DisplayManagementStatus.Succeeded;
        }

        ShowErrorIfCurrent(generation, MessageFor(status));
        return false;
    }

    private void ApplyList(IReadOnlyList<DisplayInfoPayload> displays)
    {
        DateTimeOffset now = ServerNow();
        HashSet<string> listed = new(StringComparer.Ordinal);
        int index = 0;
        foreach (DisplayInfoPayload display in displays)
        {
            if (display is null || string.IsNullOrEmpty(display.Id) || !listed.Add(display.Id))
            {
                continue;
            }

            DisplayItemViewModel? existing = Find(display.Id);
            if (existing is null)
            {
                Displays.Insert(index, new DisplayItemViewModel(display, now));
            }
            else
            {
                existing.Update(display, now);
                int current = Displays.IndexOf(existing);
                if (current != index)
                {
                    Displays.Move(current, index);
                }
            }
            index++;
        }

        while (Displays.Count > index)
        {
            Displays.RemoveAt(Displays.Count - 1);
        }
    }

    private void ApplyDisplay(DisplayInfoPayload display)
    {
        DisplayItemViewModel? existing = Find(display.Id);
        if (existing is null)
        {
            Displays.Add(new DisplayItemViewModel(display, ServerNow()));
        }
        else
        {
            existing.Update(display, ServerNow());
        }
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private void RemoveDisplay(string displayId)
    {
        DisplayItemViewModel? display = Find(displayId);
        if (display is not null)
        {
            Displays.Remove(display);
            OnPropertyChanged(nameof(EmptyVisibility));
        }
    }

    private void RestorePendingCode()
    {
        string? code = _pendingCodes?.Take(_timeProvider.GetUtcNow());
        if (code is not null && !IsPromptOpen)
        {
            OpenApproval(code);
        }
    }

    private void StartLastSeenTimer()
    {
        _lastSeenTimer ??= _timeProvider.CreateTimer(
            _ => _runOnUiThread(RefreshLastSeen),
            null,
            LAST_SEEN_REFRESH_INTERVAL,
            LAST_SEEN_REFRESH_INTERVAL);
    }

    private void StopLastSeenTimer()
    {
        _lastSeenTimer?.Dispose();
        _lastSeenTimer = null;
    }

    private DisplayItemViewModel? Find(string displayId) =>
        Displays.FirstOrDefault(display => display.Id == displayId);

    private DateTimeOffset ServerNow() => _timeProvider.GetUtcNow().AddMilliseconds(_serverClockOffsetMs());

    private void OpenPrompt(DisplayPromptMode mode, string? displayId, string displayName)
    {
        _promptGeneration++;
        _promptMode = mode;
        _promptDisplayId = displayId;
        _promptDisplayName = displayName;
        _promptError = null;
        SetNotice(null);
        NotifyPrompt();
    }

    private void ClosePrompt()
    {
        if (_promptMode == DisplayPromptMode.None)
        {
            return;
        }

        _promptGeneration++;
        _promptMode = DisplayPromptMode.None;
        _promptDisplayId = null;
        _promptDisplayName = string.Empty;
        _promptCode = string.Empty;
        _promptName = string.Empty;
        _promptError = null;
        NotifyPrompt();
        OnPropertyChanged(nameof(PromptCode));
        OnPropertyChanged(nameof(PromptName));
    }

    private void CloseIfCurrent(int generation)
    {
        if (generation == _promptGeneration)
        {
            ClosePrompt();
        }
    }

    private void ShowErrorIfCurrent(int generation, string message)
    {
        if (generation == _promptGeneration)
        {
            SetPromptError(message);
        }
    }

    private void SetPromptError(string? message)
    {
        if (_promptError != message)
        {
            _promptError = message;
            OnPropertyChanged(nameof(PromptError));
            OnPropertyChanged(nameof(PromptErrorVisibility));
        }
    }

    private void SetListError(string? message)
    {
        if (_listError != message)
        {
            _listError = message;
            OnPropertyChanged(nameof(ListError));
            OnPropertyChanged(nameof(ListErrorVisibility));
            OnPropertyChanged(nameof(EmptyVisibility));
        }
    }

    private void SetNotice(string? message)
    {
        if (_noticeMessage != message)
        {
            _noticeMessage = message;
            OnPropertyChanged(nameof(NoticeMessage));
            OnPropertyChanged(nameof(NoticeVisibility));
        }
    }

    private void NotifyPrompt()
    {
        OnPropertyChanged(nameof(PromptMode));
        OnPropertyChanged(nameof(IsPromptOpen));
        OnPropertyChanged(nameof(PromptVisibility));
        OnPropertyChanged(nameof(PromptTitle));
        OnPropertyChanged(nameof(PromptMessage));
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(CodeFieldVisibility));
        OnPropertyChanged(nameof(NameFieldVisibility));
        OnPropertyChanged(nameof(SignInHintVisibility));
        OnPropertyChanged(nameof(PromptError));
        OnPropertyChanged(nameof(PromptErrorVisibility));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
