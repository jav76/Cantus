using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Core.Models;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Sign-in state for the account owner: the authorized Sessions the server
/// reports, the active user, the connect button, subscribing to a user's
/// playback, and logout. Raises <see cref="SessionEnded"/> and
/// <see cref="LoggedOut"/> so the owner of the playback state can clear it.
/// The owner's Displays live in <see cref="Displays"/>, which follows the sign-in state.
/// </summary>
public sealed class SessionViewModel : INotifyPropertyChanged
{
    public const string SPOTIFY_LOGIN_PATH = "/api/auth/spotify/login";
    private const string NO_ACTIVE_USER_NAME = "None";
    private const string CONNECTED_GLYPH = "";
    private const string CONNECT_GLYPH = "";

    private readonly ISignalRPlaybackClient _client;

    private string _activeUserName = NO_ACTIVE_USER_NAME;
    private string? _activeUserId;
    private int _authorizedSessionsCount;

    public SessionViewModel(
        ISignalRPlaybackClient client,
        IDisplayManagementApi displaysApi,
        TimeProvider? timeProvider = null,
        Action<Action>? runOnUiThread = null,
        PendingPairCodeStore? pendingPairCodes = null)
    {
        _client = client;
        Displays = new DisplaysViewModel(
            displaysApi,
            timeProvider,
            () => _client.ClockOffsetMs,
            runOnUiThread,
            pendingPairCodes);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the server reports no Sessions or revokes the active one.</summary>
    public event Action? SessionEnded;

    /// <summary>Raised after <see cref="LogoutAsync"/> signs the owner out.</summary>
    public event Action? LoggedOut;

    public ObservableCollection<AuthorizedSessionPayload> Sessions { get; } = new();

    /// <summary>The signed-in owner's Displays and the prompt that approves a new one.</summary>
    public DisplaysViewModel Displays { get; }

    public AuthorizedSessionPayload? CurrentUserSession => Sessions.FirstOrDefault();

    public string ActiveUserName
    {
        get => _activeUserName;
        internal set
        {
            if (_activeUserName != value)
            {
                _activeUserName = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ActiveUserId
    {
        get => _activeUserId;
        internal set
        {
            if (_activeUserId != value)
            {
                _activeUserId = value;
                OnPropertyChanged();
            }
        }
    }

    public int AuthorizedSessionsCount
    {
        get => _authorizedSessionsCount;
        internal set
        {
            if (_authorizedSessionsCount != value)
            {
                _authorizedSessionsCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NoSessionsVisibility));
                OnPropertyChanged(nameof(HasSessionsVisibility));
                NotifyAuthorization();
            }
        }
    }

    public bool IsAuthorized => AuthorizedSessionsCount > 0 || HasActiveUserName;

    public string ConnectButtonText => IsAuthorized
        ? (HasActiveUserName ? $"Connected: {ActiveUserName}" : "Connected")
        : "Connect Spotify";

    public string ConnectButtonGlyph => IsAuthorized ? CONNECTED_GLYPH : CONNECT_GLYPH;

    public Visibility NoSessionsVisibility =>
        AuthorizedSessionsCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasSessionsVisibility =>
        AuthorizedSessionsCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ServerBaseUrl => _client.ServerBaseUrl;

    /// <summary>
    /// Server-relative sign-in path. Carries this client's id so the server
    /// can pair the new Session with it.
    /// </summary>
    public string SpotifyLoginPath => string.IsNullOrWhiteSpace(_client.ClientId)
        ? SPOTIFY_LOGIN_PATH
        : $"{SPOTIFY_LOGIN_PATH}?client_id={_client.ClientId}";

    private bool HasActiveUserName =>
        ActiveUserName != NO_ACTIVE_USER_NAME && !string.IsNullOrEmpty(ActiveUserName);

    /// <summary>
    /// Takes the server's Session list. The first Session becomes active and
    /// its playback is subscribed to; an empty list signs the owner out.
    /// </summary>
    public void ApplySessions(IReadOnlyList<AuthorizedSessionPayload>? sessions)
    {
        Sessions.Clear();
        if (sessions is null || sessions.Count == 0)
        {
            ClearSignIn();
            NotifySessionList();
            SessionEnded?.Invoke();
            return;
        }

        foreach (AuthorizedSessionPayload session in sessions)
        {
            if (session is not null)
            {
                Sessions.Add(session);
            }
        }
        AuthorizedSessionsCount = sessions.Count;

        AuthorizedSessionPayload? current = sessions.FirstOrDefault(s => s is not null);
        if (current is not null)
        {
            ActiveUserName = current.DisplayName;
            ActiveUserId = current.Id;
            _ = _client.SubscribeToUserAsync(current.Id);
        }

        NotifySessionList();
        BeginDisplayManagement();
    }

    /// <summary>Takes the Session created by a completed sign-in and subscribes to its playback.</summary>
    public void ApplyAuthSession(AuthorizedSessionPayload? session)
    {
        if (session is null)
        {
            return;
        }

        Sessions.Clear();
        Sessions.Add(session);
        AuthorizedSessionsCount = 1;
        ActiveUserName = session.DisplayName;
        ActiveUserId = session.Id;
        _ = _client.SubscribeToUserAsync(session.Id);
        NotifySessionList();
        BeginDisplayManagement();
    }

    /// <summary>Signs the owner out when the revoked user is the active one, or when no user is named.</summary>
    public void ApplySessionRevoked(string? userId)
    {
        if (!string.IsNullOrEmpty(userId) && ActiveUserId != userId)
        {
            return;
        }

        Sessions.Clear();
        ClearSignIn();
        NotifySessionList();
        SessionEnded?.Invoke();
    }

    /// <summary>Takes the active user a playback poll reports. A poll without a display name keeps the known one.</summary>
    public void ApplyActiveUser(string? displayName, string? userId)
    {
        if (!string.IsNullOrEmpty(displayName))
        {
            ActiveUserName = displayName;
        }
        ActiveUserId = userId;
        NotifyAuthorization();
    }

    /// <summary>Takes the Session count and active user from a server diagnostics snapshot.</summary>
    public void ApplyDiagnostics(DiagnosticsPayload diagnostics)
    {
        AuthorizedSessionsCount = diagnostics.AuthorizedSessions;
        if (!string.IsNullOrEmpty(diagnostics.ActiveUserName))
        {
            ActiveUserName = diagnostics.ActiveUserName;
        }
        ActiveUserId = diagnostics.ActiveUserId;

        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        NotifyAuthorization();
    }

    public async Task SubscribeToUserAsync(string? userId)
    {
        await _client.SubscribeToUserAsync(userId);
    }

    public async Task LogoutAsync()
    {
        await _client.LogoutAsync();
        Sessions.Clear();
        ClearSignIn();
        NotifySessionList();
        LoggedOut?.Invoke();
    }

    private void ClearSignIn()
    {
        AuthorizedSessionsCount = 0;
        ActiveUserName = NO_ACTIVE_USER_NAME;
        ActiveUserId = null;
        Displays.SetSignedIn(false);
    }

    private void BeginDisplayManagement()
    {
        Displays.SetSignedIn(true);
        _ = Displays.RefreshAsync();
    }

    private void NotifySessionList()
    {
        OnPropertyChanged(nameof(CurrentUserSession));
        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        NotifyAuthorization();
    }

    private void NotifyAuthorization()
    {
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonGlyph));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
