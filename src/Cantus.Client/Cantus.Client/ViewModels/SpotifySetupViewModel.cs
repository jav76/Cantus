using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Core.Models;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// The Connect Spotify setup screen: whether to sign in through the server's Spotify app or the
/// user's own, and the steps and checks for creating one's own app. Decides whether a sign-in
/// needs the screen at all (<see cref="NeedsDialog"/>) and builds the sign-in path.
/// </summary>
public sealed class SpotifySetupViewModel : INotifyPropertyChanged
{
    public const string DEFAULT_DASHBOARD_URL = "https://developer.spotify.com/dashboard/create";
    /// <summary>The server's sign-in failure page links to <c>/?connect=spotify</c> to open this screen.</summary>
    public const string CONNECT_QUERY_PARAMETER = "connect";
    public const string CONNECT_QUERY_VALUE = "spotify";
    public const string REVERSE_PROXY_DOCS_URL = "https://cantus.docs.jav26122.net/operator-guide/reverse-proxy/";

    private readonly ISpotifySetupApi _api;
    private readonly OwnSpotifyAppStore _ownApp;
    private readonly Func<string?, string> _buildLoginPath;

    private SpotifySetupInfoPayload? _info;
    private bool _useOwnApp;
    private string _clientIdText = string.Empty;

    public SpotifySetupViewModel(
        ISpotifySetupApi api,
        OwnSpotifyAppStore ownApp,
        Func<string?, string> buildLoginPath)
    {
        _api = api;
        _ownApp = ownApp;
        _buildLoginPath = buildLoginPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SpotifySetupInfoPayload? Info => _info;

    /// <summary>
    /// The screen only has something to offer when the server allows the user's own app. Without
    /// setup info (an unreachable or older server) sign-in goes straight to the server's app.
    /// </summary>
    public bool NeedsDialog => _info is { UserClientIdsAllowed: true };

    public bool ServerAppAvailable => _info?.ServerClientIdConfigured ?? false;

    public Visibility AppChoiceVisibility => ServerAppAvailable ? Visibility.Visible : Visibility.Collapsed;

    public string IntroText => ServerAppAvailable
        ? "Sign in through this server's Spotify app, or your own if the server's app has no room for your account."
        : "This server has no Spotify app of its own, so sign in with yours. Creating one takes about two minutes and needs a Spotify Premium account.";

    public bool UseOwnApp
    {
        get => _useOwnApp;
        set
        {
            if (_useOwnApp == value)
            {
                return;
            }

            _useOwnApp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseServerApp));
            OnPropertyChanged(nameof(OwnAppVisibility));
            NotifyClientIdState();
        }
    }

    public bool UseServerApp
    {
        get => !_useOwnApp;
        set => UseOwnApp = !value;
    }

    public Visibility OwnAppVisibility => _useOwnApp ? Visibility.Visible : Visibility.Collapsed;

    public string ClientIdText
    {
        get => _clientIdText;
        set
        {
            string next = value ?? string.Empty;
            if (_clientIdText == next)
            {
                return;
            }

            _clientIdText = next;
            OnPropertyChanged();
            NotifyClientIdState();
        }
    }

    public bool IsClientIdValid => SpotifyClientIdFormat.TryNormalize(_clientIdText, out _);

    /// <summary>Shown once something has been typed, so an empty box does not open with an error.</summary>
    public string ClientIdError =>
        _clientIdText.Trim().Length == 0 || IsClientIdValid
            ? string.Empty
            : $"A Client ID is {SpotifyClientIdFormat.LENGTH} letters and digits, from your app's Settings page.";

    public Visibility ClientIdErrorVisibility =>
        ClientIdError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool CanConnect => _useOwnApp ? IsClientIdValid : ServerAppAvailable;

    public string RedirectUri => _info?.RedirectUri ?? string.Empty;

    public Visibility RedirectUriWarningVisibility =>
        _info is { RedirectUriAccepted: false } ? Visibility.Visible : Visibility.Collapsed;

    public Uri DashboardUri =>
        Uri.TryCreate(_info?.DashboardUrl, UriKind.Absolute, out Uri? uri) ? uri : new Uri(DEFAULT_DASHBOARD_URL);

    public Uri ReverseProxyDocsUri { get; } = new(REVERSE_PROXY_DOCS_URL);

    public bool HasSavedClientId => _ownApp.ClientId is not null;

    public Visibility ForgetVisibility => HasSavedClientId ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Fetches the server's setup info and resets the screen: the saved own app is preselected,
    /// and a server without its own app always uses the user's.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _info = await _api.GetSetupInfoAsync(cancellationToken);
        string? saved = _ownApp.ClientId;
        _clientIdText = saved ?? string.Empty;
        _useOwnApp = !ServerAppAvailable || saved is not null;
        NotifyAll();
    }

    /// <summary>
    /// The sign-in path for the current choice. Choosing the own app saves its Client ID for the
    /// next sign-in. Returns null when the choice cannot sign in yet.
    /// </summary>
    public string? PrepareLoginPath()
    {
        if (!NeedsDialog)
        {
            return _buildLoginPath(null);
        }

        if (!CanConnect)
        {
            return null;
        }

        if (!_useOwnApp)
        {
            return _buildLoginPath(null);
        }

        _ownApp.Save(_clientIdText);
        OnPropertyChanged(nameof(HasSavedClientId));
        OnPropertyChanged(nameof(ForgetVisibility));
        return _buildLoginPath(_ownApp.ClientId);
    }

    public void ForgetSavedClientId()
    {
        _ownApp.Forget();
        ClientIdText = string.Empty;
        OnPropertyChanged(nameof(HasSavedClientId));
        OnPropertyChanged(nameof(ForgetVisibility));
    }

    /// <summary>Whether a page query string (with or without the leading <c>?</c>) asks to open this screen.</summary>
    public static bool IsConnectLink(string? search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return false;
        }

        foreach (string pair in search.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2
                && string.Equals(parts[0], CONNECT_QUERY_PARAMETER, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Uri.UnescapeDataString(parts[1]), CONNECT_QUERY_VALUE, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void NotifyClientIdState()
    {
        OnPropertyChanged(nameof(IsClientIdValid));
        OnPropertyChanged(nameof(ClientIdError));
        OnPropertyChanged(nameof(ClientIdErrorVisibility));
        OnPropertyChanged(nameof(CanConnect));
    }

    private void NotifyAll()
    {
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
