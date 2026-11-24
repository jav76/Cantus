using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cantus.Client.Icons;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;
using Cantus.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Sign-in state for the account owner: the authorized Sessions the server
/// reports, the active user, the account button and its menu, subscribing to a
/// user's playback, logout, and switching between a Spotify Session and a Local
/// Session. Raises <see cref="SessionEnded"/> and <see cref="LoggedOut"/> so the
/// owner of the playback state can clear it. The owner's Displays live in
/// <see cref="Displays"/>, which follows the sign-in state.
/// </summary>
public sealed class SessionViewModel : INotifyPropertyChanged
{
    public const string SPOTIFY_LOGIN_PATH = "/api/auth/spotify/login";
    public const string SPOTIFY_CLIENT_ID_QUERY = "spotify_client_id";
    public const string LOCAL_ACCOUNT_LABEL = "This computer";
    public const string SPOTIFY_ACCOUNT_LABEL = "Spotify";
    public const string CONNECT_SPOTIFY_INSTEAD_TEXT = "Connect Spotify instead";
    public const string USE_LOCAL_INSTEAD_TEXT = "Use this computer's players instead";
    public const string SIGN_OUT_TEXT = "Sign out";
    public const string NO_FOLLOWED_PLAYER_TEXT = "No player playing";
    private const string NO_ACTIVE_USER_NAME = "None";

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<SessionViewModel>();

    private readonly ISignalRPlaybackClient _client;
    private readonly ILocalPlayback? _localPlayback;
    private readonly Action<Action> _runOnUiThread;

    private string _activeUserName = NO_ACTIVE_USER_NAME;
    private string? _activeUserId;
    private int _authorizedSessionsCount;

    public SessionViewModel(
        ISignalRPlaybackClient client,
        IDisplayManagementApi displaysApi,
        TimeProvider? timeProvider = null,
        Action<Action>? runOnUiThread = null,
        PendingPairCodeStore? pendingPairCodes = null,
        ISpotifySetupApi? spotifySetupApi = null,
        OwnSpotifyAppStore? ownSpotifyApp = null,
        ILocalPlayback? localPlayback = null)
    {
        _client = client;
        _localPlayback = localPlayback;
        _runOnUiThread = runOnUiThread ?? (action => action());
        Displays = new DisplaysViewModel(
            displaysApi,
            timeProvider,
            () => _client.ClockOffsetMs,
            runOnUiThread,
            pendingPairCodes);
        SpotifySetup = new SpotifySetupViewModel(
            spotifySetupApi ?? new SpotifySetupApi(client, new HttpClient()),
            ownSpotifyApp ?? new OwnSpotifyAppStore(new LocalSettingsStore()),
            BuildSpotifyLoginPath);
        SpotifySetup.PropertyChanged += (_, _) => NotifyLocalAvailability();
        LocalSetup = new LocalSessionSetupViewModel(UseLocalPlayersAsync);
        LocalSetup.PropertyChanged += OnLocalSetupPropertyChanged;
        LocalPlayers = new LocalPlayersViewModel(localPlayback, _runOnUiThread);

        if (_localPlayback is not null)
        {
            _localPlayback.AvailabilityChanged += OnLocalAvailabilityMayHaveChanged;
            _localPlayback.FollowedPlayerChanged += () => _runOnUiThread(NotifyFollowedPlayer);
            _client.Connected += OnLocalAvailabilityMayHaveChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the server reports no Sessions or revokes the active one.</summary>
    public event Action? SessionEnded;

    /// <summary>Raised after <see cref="LogoutAsync"/> signs the owner out.</summary>
    public event Action? LoggedOut;

    public ObservableCollection<AuthorizedSessionPayload> Sessions { get; } = new();

    /// <summary>The signed-in owner's Displays and the prompt that approves a new one.</summary>
    public DisplaysViewModel Displays { get; }

    /// <summary>The Connect Spotify setup screen, for signing in with the user's own Spotify app.</summary>
    public SpotifySetupViewModel SpotifySetup { get; }

    /// <summary>The "Use this computer's players" screen: join code and refusal messages.</summary>
    public LocalSessionSetupViewModel LocalSetup { get; }

    /// <summary>The "Players on this computer" section, active only for a Local Session.</summary>
    public LocalPlayersViewModel LocalPlayers { get; }

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

    /// <summary>True when the current Session is a Local Session (this computer's players, no Spotify account).</summary>
    public bool IsLocalSession => CurrentUserSession?.IsLocal == true;

    /// <summary>
    /// This computer's players can be offered: the platform can read them and the server allows Local
    /// Sessions. False on WASM, on desktops without a source and on servers without Local Sessions.
    /// </summary>
    public bool IsLocalAvailable =>
        _localPlayback?.IsSupported == true && SpotifySetup.Info is { LocalSessionsEnabled: true };

    public bool LocalJoinCodeRequired => SpotifySetup.Info?.LocalSessionJoinCodeRequired == true;

    public string? JoinCodePrefill => _localPlayback?.JoinCodePrefill;

    /// <summary>"Use this computer's players" next to every signed-out Connect Spotify.</summary>
    public Visibility LocalChoiceVisibility =>
        IsLocalAvailable && CurrentUserSession is null && AuthorizedSessionsCount == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>False while an attempt to use this computer's players is running, so it cannot start twice.</summary>
    public bool CanUseLocalPlayers => !LocalSetup.IsBusy;

    /// <summary>
    /// The account buttons open <see cref="AccountMenuItems"/> while a Session is held, and start a
    /// Spotify sign-in otherwise.
    /// </summary>
    public bool HasAccountMenu => CurrentUserSession is not null;

    public string ConnectButtonText => IsLocalSession
        ? LOCAL_ACCOUNT_LABEL
        : IsAuthorized
            ? (HasActiveUserName ? $"Connected: {ActiveUserName}" : "Connected")
            : "Connect Spotify";

    public CantusIconKind ConnectButtonIcon => IsLocalSession
        ? CantusIconKind.Display
        : IsAuthorized ? CantusIconKind.SpotifyConnected : CantusIconKind.SpotifyConnect;

    public string AccountButtonToolTip => IsLocalSession ? LocalAccountHeader : ConnectButtonText;

    /// <summary>Where the signed-in account's playback comes from, shown under its name.</summary>
    public string AccountSourceText => IsLocalSession ? LOCAL_ACCOUNT_LABEL : SPOTIFY_ACCOUNT_LABEL;

    public CantusIconKind AccountIcon => IsLocalSession ? CantusIconKind.Display : CantusIconKind.Account;

    /// <summary>Volume, shuffle and repeat: a local player cannot be driven through them.</summary>
    public Visibility SpotifyControlsVisibility => IsLocalSession ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Find a song plays the pick on Spotify, so it is offered only for a Spotify Session.</summary>
    public Visibility SongLookupVisibility =>
        CurrentUserSession is not null && !IsLocalSession ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The app name of the player a Local Session follows, or null when none is playing.</summary>
    public string? FollowedPlayerName => _localPlayback?.FollowedPlayer?.AppName;

    public string FollowingText =>
        FollowedPlayerName is { Length: > 0 } name ? $"Following: {name}" : NO_FOLLOWED_PLAYER_TEXT;

    public IReadOnlyList<AccountMenuItem> AccountMenuItems
    {
        get
        {
            if (CurrentUserSession is null)
            {
                return [];
            }

            if (IsLocalSession)
            {
                return
                [
                    AccountMenuItem.Header(LocalAccountHeader),
                    AccountMenuItem.Header(FollowingText),
                    AccountMenuItem.Separator,
                    AccountMenuItem.Command(CONNECT_SPOTIFY_INSTEAD_TEXT, AccountMenuAction.ConnectSpotifyInstead),
                    AccountMenuItem.Command(SIGN_OUT_TEXT, AccountMenuAction.SignOut),
                ];
            }

            List<AccountMenuItem> items =
            [
                AccountMenuItem.Header($"{SPOTIFY_ACCOUNT_LABEL}: {ActiveUserName}"),
                AccountMenuItem.Separator,
            ];
            if (IsLocalAvailable)
            {
                items.Add(AccountMenuItem.Command(USE_LOCAL_INSTEAD_TEXT, AccountMenuAction.UseLocalPlayersInstead));
            }

            items.Add(AccountMenuItem.Command(SIGN_OUT_TEXT, AccountMenuAction.SignOut));
            return items;
        }
    }

    private string LocalAccountHeader => $"{LOCAL_ACCOUNT_LABEL}: {ActiveUserName}";

    public Visibility NoSessionsVisibility =>
        AuthorizedSessionsCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasSessionsVisibility =>
        AuthorizedSessionsCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Owner-only actions such as Find a song. Keyed on <see cref="CurrentUserSession"/>, not
    /// <see cref="IsAuthorized"/>: a paired Display is authorized by diagnostics but never
    /// receives the Session list.
    /// </summary>
    public Visibility OwnerActionsVisibility =>
        CurrentUserSession is not null ? Visibility.Visible : Visibility.Collapsed;

    public string ServerBaseUrl => _client.ServerBaseUrl;

    /// <summary>
    /// Server-relative sign-in path through the server's Spotify app. Carries this client's id
    /// so the server can pair the new Session with it.
    /// </summary>
    public string SpotifyLoginPath => BuildSpotifyLoginPath(null);

    /// <summary>
    /// Server-relative sign-in path. <paramref name="ownClientId"/> is the user's own Spotify
    /// app; null signs in through the server's.
    /// </summary>
    public string BuildSpotifyLoginPath(string? ownClientId)
    {
        List<string> query = new();
        if (!string.IsNullOrWhiteSpace(_client.ClientId))
        {
            query.Add($"client_id={Uri.EscapeDataString(_client.ClientId)}");
        }

        if (!string.IsNullOrWhiteSpace(ownClientId))
        {
            query.Add($"{SPOTIFY_CLIENT_ID_QUERY}={Uri.EscapeDataString(ownClientId)}");
        }

        return query.Count == 0 ? SPOTIFY_LOGIN_PATH : $"{SPOTIFY_LOGIN_PATH}?{string.Join('&', query)}";
    }

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

    /// <summary>Signs out. The account controls return to the signed-out choice; nothing signs in again on its own.</summary>
    public async Task LogoutAsync()
    {
        _localPlayback?.NotifySignedOut();
        await _client.LogoutAsync();
        Sessions.Clear();
        ClearSignIn();
        NotifySessionList();
        LoggedOut?.Invoke();
    }

    /// <summary>Signs out of the current Session, then starts the Spotify sign-in.</summary>
    public async Task SwitchToSpotifyAsync(Func<Task> startSpotifySignIn)
    {
        await LogoutAsync();
        await startSpotifySignIn();
    }

    /// <summary>
    /// Creates a Local Session for this computer and connects with it. Any Session held is signed out
    /// only once the server accepts the new one, so a refusal keeps the owner signed in.
    /// </summary>
    public async Task<LocalSessionResult> UseLocalPlayersAsync(string? joinCode)
    {
        if (_localPlayback is null)
        {
            return new LocalSessionResult(LocalSessionStatus.Failed);
        }

        Func<Task>? signOutHeld = CurrentUserSession is not null || !string.IsNullOrWhiteSpace(_client.SessionToken)
            ? LogoutAsync
            : null;
        return await _localPlayback.CreateSessionAsync(joinCode, signOutHeld);
    }

    /// <summary>
    /// Fetches the server's setup info when this computer's players can be read, to learn whether the
    /// server allows them.
    /// </summary>
    public async Task RefreshLocalAvailabilityAsync()
    {
        if (_localPlayback?.IsSupported != true)
        {
            NotifyLocalAvailability();
            return;
        }

        try
        {
            await SpotifySetup.RefreshInfoAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read whether the server allows this computer's players");
        }
    }

    private void OnLocalSetupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(LocalSessionSetupViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(CanUseLocalPlayers));
        }
    }

    private void OnLocalAvailabilityMayHaveChanged() => _runOnUiThread(() => _ = RefreshLocalAvailabilityAsync());

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
        OnPropertyChanged(nameof(OwnerActionsVisibility));
        OnPropertyChanged(nameof(NoSessionsVisibility));
        OnPropertyChanged(nameof(HasSessionsVisibility));
        NotifyAuthorization();
    }

    private void NotifyAuthorization()
    {
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectButtonIcon));
        NotifySessionKind();
    }

    private void NotifySessionKind()
    {
        LocalPlayers.SetActive(IsLocalSession);
        OnPropertyChanged(nameof(IsLocalSession));
        OnPropertyChanged(nameof(HasAccountMenu));
        OnPropertyChanged(nameof(AccountButtonToolTip));
        OnPropertyChanged(nameof(AccountSourceText));
        OnPropertyChanged(nameof(AccountIcon));
        OnPropertyChanged(nameof(SpotifyControlsVisibility));
        OnPropertyChanged(nameof(SongLookupVisibility));
        OnPropertyChanged(nameof(LocalChoiceVisibility));
        OnPropertyChanged(nameof(AccountMenuItems));
    }

    private void NotifyLocalAvailability()
    {
        OnPropertyChanged(nameof(IsLocalAvailable));
        OnPropertyChanged(nameof(LocalJoinCodeRequired));
        OnPropertyChanged(nameof(LocalChoiceVisibility));
        OnPropertyChanged(nameof(AccountMenuItems));
    }

    private void NotifyFollowedPlayer()
    {
        OnPropertyChanged(nameof(FollowedPlayerName));
        OnPropertyChanged(nameof(FollowingText));
        OnPropertyChanged(nameof(AccountMenuItems));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
