using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

public enum ServerSetupResult
{
    /// <summary>Nothing was saved: the address is invalid, locked, or unreachable and not yet confirmed.</summary>
    NotSaved,

    /// <summary>The choice was saved and the app already points at that server.</summary>
    Unchanged,

    /// <summary>The choice was saved and names a different server than the one this launch resolved.</summary>
    Changed,
}

public enum ServerCheckState
{
    None,
    Checking,
    Reachable,
    Unreachable,
}

/// <summary>
/// The desktop app's server address form, shared by the first-run prompt and the Server setting.
/// Saving checks that a Cantus server answers; an unreachable address is saved on a second,
/// deliberate Save.
/// </summary>
public sealed class ServerSetupViewModel : INotifyPropertyChanged
{
    public const string SAVE_TEXT = "Connect";
    public const string SAVE_ANYWAY_TEXT = "Save anyway";
    public const string INVALID_ADDRESS_TEXT = "Enter a full address that starts with http:// or https://, such as http://192.168.0.10:5000.";
    public const string SKIP_TEXT = "Skip";
    public const string RUN_HERE_TEXT = "Run it on this computer";
    public const string LOCAL_SERVER_RUNNING_TEXT = "Cantus runs its own server on this computer. It keeps running after you close the app.";
    public const string LOCAL_SERVER_STOPPED_TEXT = "The server on this computer is stopped. It starts again the next time Cantus opens.";
    public const string LOCAL_SERVER_NOT_STOPPED_TEXT = "This app did not start the server on this computer, so it cannot stop it.";

    private readonly ServerSettingsStore _settings;
    private readonly ServerSelection _current;
    private readonly IServerProbe _probe;
    private readonly Action? _forgetCredentials;
    private readonly ILocalServer? _localServer;

    private string _addressText;
    private ServerCheckState _checkState = ServerCheckState.None;
    private string _checkedBaseUrl = string.Empty;
    private string _localServerText = LOCAL_SERVER_RUNNING_TEXT;
    private bool _isStoppingLocalServer;

    /// <param name="current">The server this launch resolved.</param>
    /// <param name="forgetCredentials">Forgets the Session and Display pairing saved on this device; run when the server changes.</param>
    /// <param name="localServer">The server shipped with this app, started before this computer's address is checked.</param>
    public ServerSetupViewModel(
        ServerSettingsStore settings,
        ServerSelection current,
        IServerProbe probe,
        Action? forgetCredentials = null,
        ILocalServer? localServer = null)
    {
        _settings = settings;
        _current = current;
        _probe = probe;
        _forgetCredentials = forgetCredentials;
        _localServer = localServer;
        _addressText = current.NeedsSetup ? string.Empty : current.BaseUrl;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AddressText
    {
        get => _addressText;
        set
        {
            string text = value ?? string.Empty;
            if (_addressText == text)
            {
                return;
            }

            _addressText = text;
            _checkState = ServerCheckState.None;
            _checkedBaseUrl = string.Empty;
            OnPropertyChanged(string.Empty);
        }
    }

    public bool IsAddressValid => ServerAddress.TryNormalize(_addressText, out _);

    /// <summary>Empty while the box is empty, so the prompt does not open with an error.</summary>
    public string AddressError =>
        string.IsNullOrWhiteSpace(_addressText) || IsAddressValid ? string.Empty : INVALID_ADDRESS_TEXT;

    public Visibility AddressErrorVisibility => ToVisibility(AddressError.Length > 0);

    /// <summary>The address came from the command line or environment, which settings cannot outrank.</summary>
    public bool IsLocked => _current.IsOverridden;

    public bool IsEditable => !IsLocked;

    public string LockedText => _current.Source switch
    {
        ServerUrlSource.CommandLine => "This launch uses the address from --server-url. Start Cantus without it to change the server here.",
        ServerUrlSource.EnvironmentVariable => "This launch uses the address from CANTUS_SERVER_URL. Unset it to change the server here.",
        _ => string.Empty,
    };

    public Visibility LockedVisibility => ToVisibility(IsLocked);

    public ServerCheckState CheckState => _checkState;

    public bool IsChecking => _checkState == ServerCheckState.Checking;

    public bool CanSave => IsAddressValid && !IsLocked && !IsChecking;

    public string SaveButtonText => _checkState == ServerCheckState.Unreachable ? SAVE_ANYWAY_TEXT : SAVE_TEXT;

    public string StatusText => _checkState switch
    {
        ServerCheckState.Checking when ManagesLocally(_checkedBaseUrl) => "Starting Cantus on this computer...",
        ServerCheckState.Checking => "Looking for a Cantus server...",
        ServerCheckState.Reachable => "Cantus server found.",
        ServerCheckState.Unreachable =>
            $"No Cantus server answered at {_checkedBaseUrl}. Check the address and that the server is running, or save it anyway.",
        _ => string.Empty,
    };

    public Visibility StatusVisibility => ToVisibility(_checkState != ServerCheckState.None);

    public bool IsStatusWarning => _checkState == ServerCheckState.Unreachable;

    /// <summary>
    /// A server change takes effect on restart and forgets this device's sign-in and Display pairing,
    /// so the form says so before the user saves.
    /// </summary>
    public Visibility ChangeWarningVisibility => ToVisibility(
        !_current.NeedsSetup
        && !IsLocked
        && ServerAddress.TryNormalize(_addressText, out string baseUrl)
        && !ServerAddress.AreSame(baseUrl, _current.BaseUrl));

    /// <summary>True when this app ships its own server, so this computer needs no server set up first.</summary>
    public bool HasLocalServer => ManagesLocally(ServerAddress.DEFAULT_BASE_URL);

    public string SkipButtonText => HasLocalServer ? RUN_HERE_TEXT : SKIP_TEXT;

    public string SkipHintText => HasLocalServer
        ? $"No server yet? Cantus can run its own on this computer, at {ServerAddress.DEFAULT_BASE_URL}."
        : $"Skip if the server runs on this computer. Cantus then uses {ServerAddress.DEFAULT_BASE_URL}.";

    /// <summary>Shown in the Server setting while this launch uses the server this app runs itself.</summary>
    public Visibility LocalServerVisibility => ToVisibility(!_current.NeedsSetup && ManagesLocally(_current.BaseUrl));

    public string LocalServerText => _localServerText;

    public bool CanStopLocalServer => !_isStoppingLocalServer;

    /// <summary>Fills in this computer's address, the same server a skipped prompt uses.</summary>
    public void UseThisComputer()
    {
        AddressText = ServerAddress.DEFAULT_BASE_URL;
    }

    /// <summary>
    /// Saves the typed address. The first Save of an address no server answers at returns
    /// <see cref="ServerSetupResult.NotSaved"/> and shows why; saving the same address again keeps it.
    /// </summary>
    public async Task<ServerSetupResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsLocked || IsChecking || !ServerAddress.TryNormalize(_addressText, out string baseUrl))
        {
            return ServerSetupResult.NotSaved;
        }

        bool isConfirmed = _checkState == ServerCheckState.Unreachable && _checkedBaseUrl == baseUrl;
        if (!isConfirmed)
        {
            SetCheckState(ServerCheckState.Checking, baseUrl);
            bool isReachable;
            try
            {
                if (ManagesLocally(baseUrl))
                {
                    await _localServer!.EnsureRunningAsync(baseUrl, cancellationToken);
                }

                isReachable = await _probe.IsReachableAsync(baseUrl, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                SetCheckState(ServerCheckState.None, string.Empty);
                throw;
            }

            if (!isReachable)
            {
                SetCheckState(ServerCheckState.Unreachable, baseUrl);
                return ServerSetupResult.NotSaved;
            }

            SetCheckState(ServerCheckState.Reachable, baseUrl);
        }

        _settings.Save(baseUrl);

        return Complete(baseUrl);
    }

    /// <summary>Skips the prompt: this computer is used and the prompt is not shown again.</summary>
    public ServerSetupResult Skip()
    {
        if (IsLocked)
        {
            return ServerSetupResult.NotSaved;
        }

        _settings.SkipSetup();

        return Complete(ServerAddress.DEFAULT_BASE_URL);
    }

    /// <summary>Stops the server this app runs on this computer, and says whether it could.</summary>
    public async Task StopLocalServerAsync(CancellationToken cancellationToken = default)
    {
        if (_localServer is null || _isStoppingLocalServer)
        {
            return;
        }

        _isStoppingLocalServer = true;
        OnPropertyChanged(string.Empty);
        try
        {
            bool isStopped = await _localServer.StopAsync(_current.BaseUrl, cancellationToken);
            _localServerText = isStopped ? LOCAL_SERVER_STOPPED_TEXT : LOCAL_SERVER_NOT_STOPPED_TEXT;
        }
        finally
        {
            _isStoppingLocalServer = false;
            OnPropertyChanged(string.Empty);
        }
    }

    private bool ManagesLocally(string baseUrl) => _localServer?.Manages(baseUrl) == true;

    private ServerSetupResult Complete(string baseUrl)
    {
        if (ServerAddress.AreSame(baseUrl, _current.BaseUrl))
        {
            return ServerSetupResult.Unchanged;
        }

        _forgetCredentials?.Invoke();

        return ServerSetupResult.Changed;
    }

    private void SetCheckState(ServerCheckState state, string checkedBaseUrl)
    {
        _checkState = state;
        _checkedBaseUrl = checkedBaseUrl;
        OnPropertyChanged(string.Empty);
    }

    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
