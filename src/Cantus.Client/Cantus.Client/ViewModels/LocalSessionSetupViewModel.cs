using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// The "Use this computer's players" screen: the join code when the server needs one, and a one-line
/// message when the server refuses. A first try without the screen goes through
/// <see cref="ConnectAsync"/> too, so the screen opens already showing why it is needed.
/// </summary>
public sealed class LocalSessionSetupViewModel : INotifyPropertyChanged
{
    public const string INTRO_TEXT = "Cantus follows the media players on this computer. No Spotify account needed.";
    public const string JOIN_CODE_INTRO_TEXT = "This server needs a join code. Ask the person who runs it.";
    public const string PRIVACY_TEXT = "This computer's name and the titles you play are sent to the Cantus server.";
    public const string JOIN_CODE_MISSING_TEXT = "Enter the server's join code.";
    public const string JOIN_CODE_INVALID_TEXT = "That join code is not right. Check it and try again.";
    public const string DISABLED_TEXT = "This server does not allow using this computer's players.";
    public const string RATE_LIMITED_TEXT = "Too many tries. Wait a few minutes, then try again.";
    public const string LIMIT_REACHED_TEXT = "This server has no room for another computer right now.";
    public const string INVALID_NAME_TEXT = "The server refused this computer's name.";
    public const string FAILED_TEXT = "Could not reach the server. Check the connection and try again.";

    private readonly Func<string?, Task<LocalSessionResult>> _create;

    private string _joinCodeText = string.Empty;
    private bool _isJoinCodeVisible;
    private string _errorText = string.Empty;
    private bool _isBusy;

    /// <param name="create">Signs out of any Session held, then creates the Local Session with the join code.</param>
    public LocalSessionSetupViewModel(Func<string?, Task<LocalSessionResult>> create)
    {
        _create = create;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string IntroText => _isJoinCodeVisible ? JOIN_CODE_INTRO_TEXT : INTRO_TEXT;

    public string PrivacyText => PRIVACY_TEXT;

    public string PrimaryButtonText => _isJoinCodeVisible ? "Connect" : "Try again";

    public bool IsJoinCodeVisible => _isJoinCodeVisible;

    public Visibility JoinCodeVisibility => _isJoinCodeVisible ? Visibility.Visible : Visibility.Collapsed;

    public string JoinCodeText
    {
        get => _joinCodeText;
        set
        {
            string next = value ?? string.Empty;
            if (_joinCodeText == next)
            {
                return;
            }

            _joinCodeText = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConnect));
        }
    }

    public string ErrorText => _errorText;

    public Visibility ErrorVisibility => _errorText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy => _isBusy;

    public bool CanConnect => !_isBusy && (!_isJoinCodeVisible || _joinCodeText.Trim().Length > 0);

    /// <summary>Starts a fresh attempt. The join code field shows up front when the server said it needs one.</summary>
    public void Reset(bool joinCodeRequired, string? joinCodePrefill)
    {
        _isJoinCodeVisible = joinCodeRequired;
        _joinCodeText = joinCodePrefill?.Trim() ?? string.Empty;
        _errorText = string.Empty;
        _isBusy = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Creates the Local Session. Returns true once the server accepted it. A join code refusal shows the
    /// join code field; every refusal leaves a message in <see cref="ErrorText"/>.
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        if (_isBusy)
        {
            return false;
        }

        string? joinCode = _isJoinCodeVisible ? _joinCodeText.Trim() : null;
        if (_isJoinCodeVisible && string.IsNullOrEmpty(joinCode))
        {
            SetError(JOIN_CODE_MISSING_TEXT);
            return false;
        }

        SetBusy(true);
        SetError(string.Empty);
        LocalSessionStatus status;
        try
        {
            status = (await _create(joinCode)).Status;
        }
        catch (Exception)
        {
            status = LocalSessionStatus.Failed;
        }
        finally
        {
            SetBusy(false);
        }

        if (status == LocalSessionStatus.Created)
        {
            return true;
        }

        bool joinCodeRefused = status is LocalSessionStatus.JoinCodeRequired or LocalSessionStatus.JoinCodeInvalid;
        if (joinCodeRefused && !_isJoinCodeVisible)
        {
            _isJoinCodeVisible = true;
            NotifyJoinCodeVisibility();
        }

        SetError(DescribeFailure(status, joinCode));
        return false;
    }

    public static string DescribeFailure(LocalSessionStatus status, string? joinCodeSent = null) => status switch
    {
        LocalSessionStatus.Created => string.Empty,
        LocalSessionStatus.JoinCodeRequired => JOIN_CODE_MISSING_TEXT,
        LocalSessionStatus.JoinCodeInvalid when string.IsNullOrEmpty(joinCodeSent) => JOIN_CODE_MISSING_TEXT,
        LocalSessionStatus.JoinCodeInvalid => JOIN_CODE_INVALID_TEXT,
        LocalSessionStatus.Disabled => DISABLED_TEXT,
        LocalSessionStatus.RateLimited => RATE_LIMITED_TEXT,
        LocalSessionStatus.LimitReached => LIMIT_REACHED_TEXT,
        LocalSessionStatus.InvalidDisplayName => INVALID_NAME_TEXT,
        _ => FAILED_TEXT,
    };

    private void SetError(string text)
    {
        if (_errorText == text)
        {
            return;
        }

        _errorText = text;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(ErrorVisibility));
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanConnect));
    }

    private void NotifyJoinCodeVisibility()
    {
        OnPropertyChanged(nameof(IsJoinCodeVisible));
        OnPropertyChanged(nameof(JoinCodeVisibility));
        OnPropertyChanged(nameof(IntroText));
        OnPropertyChanged(nameof(PrimaryButtonText));
        OnPropertyChanged(nameof(CanConnect));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
