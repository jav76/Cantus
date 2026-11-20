using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// This client's side of becoming a Display: the pairing screen (code, QR and
/// approval link), the stored Display token, and whether owner-only controls
/// show. Raises <see cref="DisplayEnded"/> when a Display loses its token, so
/// the owner of the playback state can clear it.
/// </summary>
public sealed class DisplayPairingViewModel : INotifyPropertyChanged
{
    /// <summary>Query parameter on the app's base URL that carries a code to approve.</summary>
    public const string PAIR_QUERY_PARAMETER = "pair";

    public const string REVOKED_MESSAGE = "This display was removed by its owner. Pair it again to keep showing lyrics.";
    public const string REJECTED_MESSAGE = "This display's pairing is no longer valid. Pair it again to keep showing lyrics.";
    public const string RATE_LIMITED_MESSAGE = "Too many pairing requests. Trying again shortly.";
    public const string UNAVAILABLE_MESSAGE = "The server has too many pending pairings. Trying again shortly.";
    public const string FAILED_MESSAGE = "Could not get a pairing code. Trying again shortly.";
    public const string WAITING_FOR_SERVER_TEXT = "Connecting to the Cantus server...";
    public const string REQUESTING_CODE_TEXT = "Getting a pairing code...";

    private const string CONNECTED_STATE = "Connected";
    private const int CODE_GROUP_MIN_LENGTH = 6;
    private const char CODE_GROUP_SEPARATOR = '-';

    private static readonly TimeSpan COUNTDOWN_INTERVAL = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RETRY_DELAY = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RATE_LIMITED_RETRY_DELAY = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MIN_CODE_LIFETIME = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MAX_CODE_LIFETIME = TimeSpan.FromHours(1);

    private readonly ISignalRPlaybackClient _client;
    private readonly IDisplayPairingApi _api;
    private readonly DisplayCredentialStore _credentials;
    private readonly TimeProvider _timeProvider;
    private readonly Action<Action> _runOnUiThread;

    private DisplayPairingState _state = DisplayPairingState.Unpaired;
    private DisplayPairingCodePayload? _pairing;
    private QrCodeMatrix? _qrCode;
    private string? _approvalUrl;
    private string? _displayId;
    private string? _displayName;
    private string? _noticeMessage;
    private string? _errorMessage;
    private string _expiresInText = string.Empty;
    private long _codeDeadlineTimestamp;
    private ITimer? _timer;
    private int _requestGeneration;

    public DisplayPairingViewModel(
        ISignalRPlaybackClient client,
        IDisplayPairingApi api,
        DisplayCredentialStore credentials,
        TimeProvider? timeProvider = null,
        Action<Action>? runOnUiThread = null)
    {
        _client = client;
        _api = api;
        _credentials = credentials;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _runOnUiThread = runOnUiThread ?? (action => action());

        DisplayCredential? stored = _credentials.Load();
        if (stored is not null)
        {
            _client.DisplayToken = stored.Token;
            _displayId = stored.DisplayId;
            _displayName = stored.Name;
            _state = DisplayPairingState.Paired;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after a Display loses its token (revoked or rejected) and leaves the lyrics view.</summary>
    public event Action? DisplayEnded;

    public DisplayPairingState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDisplay));
                OnPropertyChanged(nameof(CanControlPlayback));
                OnPropertyChanged(nameof(IsPairingScreenOpen));
                OnPropertyChanged(nameof(PairingScreenVisibility));
                OnPropertyChanged(nameof(MainContentVisibility));
                OnPropertyChanged(nameof(OwnerControlsVisibility));
                OnPropertyChanged(nameof(DisplayInfoVisibility));
                OnPropertyChanged(nameof(CodeVisibility));
                OnPropertyChanged(nameof(ProgressVisibility));
                OnPropertyChanged(nameof(PairingProgressText));
            }
        }
    }

    /// <summary>Whether this client is a paired Display, showing its owner's playback view-only.</summary>
    public bool IsDisplay => State == DisplayPairingState.Paired;

    /// <summary>Whether owner-only commands (transport, seeking, offsets, latency, account) are offered.</summary>
    public bool CanControlPlayback => !IsDisplay;

    public bool IsPairingScreenOpen => State is DisplayPairingState.RequestingCode
        or DisplayPairingState.ShowingCode
        or DisplayPairingState.CodeExpired
        or DisplayPairingState.RequestFailed;

    public Visibility PairingScreenVisibility => IsPairingScreenOpen ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MainContentVisibility => IsPairingScreenOpen ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Collapses owner-only controls on a Display: they are hidden, not just disabled.</summary>
    public Visibility OwnerControlsVisibility => IsDisplay ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DisplayInfoVisibility => IsDisplay ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The code and QR show only while the code can still be approved.</summary>
    public Visibility CodeVisibility => State == DisplayPairingState.ShowingCode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProgressVisibility => State is DisplayPairingState.RequestingCode or DisplayPairingState.CodeExpired
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string PairingProgressText => IsHubConnected ? REQUESTING_CODE_TEXT : WAITING_FOR_SERVER_TEXT;

    /// <summary>The raw code, as the server issued it.</summary>
    public string? Code => _pairing?.Code;

    /// <summary>The code split into two groups so it reads easily from a distance.</summary>
    public string CodeText => FormatCode(_pairing?.Code);

    /// <summary>Link to this Cantus server where a signed-in owner approves the code.</summary>
    public string? ApprovalUrl => _approvalUrl;

    /// <summary>The server address to show next to the code for typing it in by hand.</summary>
    public string ServerBaseUrl => _client.ServerBaseUrl;

    public QrCodeMatrix? QrCode => _qrCode;

    public string ExpiresInText => _expiresInText;

    public string StatusMessage => _errorMessage ?? _noticeMessage ?? string.Empty;

    public Visibility StatusMessageVisibility =>
        string.IsNullOrEmpty(StatusMessage) ? Visibility.Collapsed : Visibility.Visible;

    public string? DisplayId => _displayId;

    public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? "This display" : _displayName;

    private bool IsHubConnected =>
        _client.State == HubConnectionState.Connected && !string.IsNullOrWhiteSpace(_client.ConnectionId);

    /// <summary>Opens the pairing screen and requests a code once the hub connection is up.</summary>
    public async Task StartPairingAsync()
    {
        if (IsDisplay)
        {
            return;
        }

        SetNotice(null);
        await RequestCodeAsync();
    }

    /// <summary>Closes the pairing screen. The pending code is abandoned and expires on the server.</summary>
    public void CancelPairing()
    {
        if (!IsPairingScreenOpen)
        {
            return;
        }

        _requestGeneration++;
        StopTimer();
        ClearCode();
        SetNotice(null);
        SetError(null);
        State = DisplayPairingState.Unpaired;
    }

    /// <summary>
    /// Takes the hub connection state. A pending code belongs to the connection that
    /// requested it, so a new connection requests a new code.
    /// </summary>
    public void ApplyConnectionState(string? connectionState)
    {
        if (!IsPairingScreenOpen)
        {
            return;
        }

        if (connectionState == CONNECTED_STATE)
        {
            _ = RequestCodeAsync();
            return;
        }

        _requestGeneration++;
        StopTimer();
        ClearCode();
        State = DisplayPairingState.RequestingCode;
        OnPropertyChanged(nameof(PairingProgressText));
    }

    /// <summary>
    /// Takes the token an owner's approval sent to this connection: stores it on the
    /// device and reconnects as the Display.
    /// </summary>
    public void ApplyDisplayPaired(DisplayPairedPayload? pairing)
    {
        if (!IsPairingScreenOpen
            || pairing is null
            || string.IsNullOrWhiteSpace(pairing.Token)
            || _pairing is null
            || pairing.PairingId != _pairing.PairingId)
        {
            return;
        }

        _requestGeneration++;
        StopTimer();
        _credentials.Save(new DisplayCredential(pairing.DisplayId, pairing.Name, pairing.Token));
        _displayId = pairing.DisplayId;
        _displayName = pairing.Name;
        ClearCode();
        SetNotice(null);
        SetError(null);
        OnPropertyChanged(nameof(DisplayId));
        OnPropertyChanged(nameof(DisplayName));
        State = DisplayPairingState.Paired;
        _ = _client.ReconnectWithDisplayTokenAsync(pairing.Token);
    }

    /// <summary>Takes a revoke for this Display: the token is discarded and the pairing screen returns.</summary>
    public void ApplyDisplayRevoked(string? displayId)
    {
        if (!IsDisplay)
        {
            return;
        }

        if (!string.IsNullOrEmpty(displayId) && !string.IsNullOrEmpty(_displayId) && displayId != _displayId)
        {
            return;
        }

        EndDisplay(REVOKED_MESSAGE);
    }

    /// <summary>
    /// Takes the server's Session list. A Display connection never receives one, so a
    /// list arriving while this client holds a Display token means the token was not
    /// accepted. An empty list means it was rejected; a non-empty one means an owner
    /// Session on this device took precedence, so the Display token is dropped quietly.
    /// </summary>
    public void ApplySessions(IReadOnlyList<AuthorizedSessionPayload>? sessions)
    {
        if (!IsDisplay)
        {
            return;
        }

        if (sessions is null || sessions.Count == 0)
        {
            EndDisplay(REJECTED_MESSAGE);
            return;
        }

        ForgetCredential();
        State = DisplayPairingState.Unpaired;
        DisplayEnded?.Invoke();
        _ = _client.ReconnectWithDisplayTokenAsync(null);
    }

    public static string FormatCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        string trimmed = code.Trim();
        if (trimmed.Length < CODE_GROUP_MIN_LENGTH)
        {
            return trimmed;
        }

        int split = (trimmed.Length + 1) / 2;
        return $"{trimmed[..split]}{CODE_GROUP_SEPARATOR}{trimmed[split..]}";
    }

    /// <summary>The link a signed-in owner opens to approve a code on this server.</summary>
    public static string BuildApprovalUrl(string serverBaseUrl, string code)
    {
        string baseUrl = serverBaseUrl.TrimEnd('/');
        return $"{baseUrl}/?{PAIR_QUERY_PARAMETER}={Uri.EscapeDataString(code)}";
    }

    private void EndDisplay(string message)
    {
        ForgetCredential();
        SetNotice(message);
        SetError(null);
        State = DisplayPairingState.RequestingCode;
        DisplayEnded?.Invoke();
        _ = _client.ReconnectWithDisplayTokenAsync(null);
    }

    private void ForgetCredential()
    {
        _credentials.Clear();
        _client.DisplayToken = null;
        _displayId = null;
        _displayName = null;
        OnPropertyChanged(nameof(DisplayId));
        OnPropertyChanged(nameof(DisplayName));
    }

    private async Task RequestCodeAsync()
    {
        int generation = ++_requestGeneration;
        StopTimer();
        ClearCode();
        if (State != DisplayPairingState.CodeExpired)
        {
            State = DisplayPairingState.RequestingCode;
        }
        OnPropertyChanged(nameof(PairingProgressText));

        string? connectionId = _client.ConnectionId;
        if (!IsHubConnected || connectionId is null)
        {
            // The Connected notification for the next connection requests the code.
            State = DisplayPairingState.RequestingCode;
            return;
        }

        DisplayPairingRequestResult result = await _api.RequestPairingAsync(connectionId);
        if (generation != _requestGeneration || !IsPairingScreenOpen)
        {
            return;
        }

        if (result.Status == DisplayPairingRequestStatus.Created && result.Pairing is not null)
        {
            ShowCode(result.Pairing);
        }
        else
        {
            ScheduleRetry(result.Status);
        }
    }

    private void ShowCode(DisplayPairingCodePayload pairing)
    {
        _pairing = pairing;
        _approvalUrl = BuildApprovalUrl(_client.ServerBaseUrl, pairing.Code);
        try
        {
            _qrCode = QrCodeMatrix.Encode(_approvalUrl);
        }
        catch (Exception)
        {
            // The code and link still show; only the QR is missing.
            _qrCode = null;
        }

        // The server's expiry is turned into a lifetime once, with the measured clock
        // offset, so a wrong device clock cannot expire the code early or never.
        DateTimeOffset serverNow = _timeProvider.GetUtcNow().AddMilliseconds(_client.ClockOffsetMs);
        TimeSpan lifetime = pairing.ExpiresAtUtc - serverNow;
        if (lifetime < MIN_CODE_LIFETIME)
        {
            lifetime = MIN_CODE_LIFETIME;
        }
        else if (lifetime > MAX_CODE_LIFETIME)
        {
            lifetime = MAX_CODE_LIFETIME;
        }
        _codeDeadlineTimestamp = _timeProvider.GetTimestamp()
            + (long)(lifetime.TotalSeconds * _timeProvider.TimestampFrequency);

        SetError(null);
        NotifyCode();
        State = DisplayPairingState.ShowingCode;
        UpdateExpiresIn();
        StartTimer(COUNTDOWN_INTERVAL, COUNTDOWN_INTERVAL, OnCountdownTick);
    }

    private void OnCountdownTick()
    {
        if (State != DisplayPairingState.ShowingCode)
        {
            return;
        }

        if (RemainingCodeLifetime() <= TimeSpan.Zero)
        {
            State = DisplayPairingState.CodeExpired;
            _ = RequestCodeAsync();
            return;
        }

        UpdateExpiresIn();
    }

    private void ScheduleRetry(DisplayPairingRequestStatus status)
    {
        (string message, TimeSpan delay) = status switch
        {
            DisplayPairingRequestStatus.RateLimited => (RATE_LIMITED_MESSAGE, RATE_LIMITED_RETRY_DELAY),
            DisplayPairingRequestStatus.Unavailable => (UNAVAILABLE_MESSAGE, RETRY_DELAY),
            _ => (FAILED_MESSAGE, RETRY_DELAY),
        };

        SetError(message);
        State = DisplayPairingState.RequestFailed;
        StartTimer(delay, Timeout.InfiniteTimeSpan, () =>
        {
            if (State == DisplayPairingState.RequestFailed)
            {
                _ = RequestCodeAsync();
            }
        });
    }

    private TimeSpan RemainingCodeLifetime() =>
        _timeProvider.GetElapsedTime(_timeProvider.GetTimestamp(), _codeDeadlineTimestamp);

    private void UpdateExpiresIn()
    {
        TimeSpan remaining = RemainingCodeLifetime();
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        int totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
        string text = $"Code expires in {totalSeconds / 60}:{totalSeconds % 60:D2}";
        if (_expiresInText != text)
        {
            _expiresInText = text;
            OnPropertyChanged(nameof(ExpiresInText));
        }
    }

    private void StartTimer(TimeSpan dueTime, TimeSpan period, Action onTick)
    {
        StopTimer();
        _timer = _timeProvider.CreateTimer(_ => _runOnUiThread(onTick), null, dueTime, period);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void ClearCode()
    {
        if (_pairing is null && _qrCode is null)
        {
            return;
        }

        _pairing = null;
        _approvalUrl = null;
        _qrCode = null;
        _expiresInText = string.Empty;
        NotifyCode();
        OnPropertyChanged(nameof(ExpiresInText));
    }

    private void NotifyCode()
    {
        OnPropertyChanged(nameof(Code));
        OnPropertyChanged(nameof(CodeText));
        OnPropertyChanged(nameof(ApprovalUrl));
        OnPropertyChanged(nameof(QrCode));
    }

    private void SetNotice(string? message)
    {
        if (_noticeMessage != message)
        {
            _noticeMessage = message;
            NotifyStatusMessage();
        }
    }

    private void SetError(string? message)
    {
        if (_errorMessage != message)
        {
            _errorMessage = message;
            NotifyStatusMessage();
        }
    }

    private void NotifyStatusMessage()
    {
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(StatusMessageVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
