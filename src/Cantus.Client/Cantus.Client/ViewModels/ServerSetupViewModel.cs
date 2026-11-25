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

    private readonly ServerSettingsStore _settings;
    private readonly ServerSelection _current;
    private readonly IServerProbe _probe;
    private readonly Action? _forgetCredentials;

    private string _addressText;
    private ServerCheckState _checkState = ServerCheckState.None;
    private string _checkedBaseUrl = string.Empty;

    /// <param name="current">The server this launch resolved.</param>
    /// <param name="forgetCredentials">Forgets the Session and Display pairing saved on this device; run when the server changes.</param>
    public ServerSetupViewModel(
        ServerSettingsStore settings,
        ServerSelection current,
        IServerProbe probe,
        Action? forgetCredentials = null)
    {
        _settings = settings;
        _current = current;
        _probe = probe;
        _forgetCredentials = forgetCredentials;
        _addressText = current.NeedsSetup ? string.Empty : current.BaseUrl;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DefaultServerUrl => ServerAddress.DEFAULT_BASE_URL;

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
