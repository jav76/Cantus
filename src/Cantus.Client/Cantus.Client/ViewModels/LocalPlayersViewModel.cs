using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cantus.Client.LocalPlayback;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// The "Players on this computer" section, shown only for a Local Session: every player app seen while
/// a Local Session is current, with its on/off switch, and which one is followed. Switching a player writes
/// <see cref="LocalPlayerPreferences"/> through <see cref="ILocalPlayback.SetPlayerEnabled"/>.
/// </summary>
public sealed class LocalPlayersViewModel : INotifyPropertyChanged
{
    public const string EMPTY_TEXT = "No media players found yet. Start one and it shows up here.";

    private readonly ILocalPlayback? _playback;
    private readonly Action<Action> _runOnUiThread;
    private readonly Dictionary<string, LocalPlayerItemViewModel> _byAppId = new(StringComparer.OrdinalIgnoreCase);

    private bool _isActive;

    public LocalPlayersViewModel(ILocalPlayback? playback, Action<Action>? runOnUiThread = null)
    {
        _playback = playback;
        _runOnUiThread = runOnUiThread ?? (action => action());
        if (_playback is not null)
        {
            _playback.AvailabilityChanged += OnPlaybackChanged;
            _playback.PlayersChanged += OnPlaybackChanged;
            _playback.FollowedPlayerChanged += OnPlaybackChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>One row per player app, in the order they were first seen.</summary>
    public ObservableCollection<LocalPlayerItemViewModel> Players { get; } = new();

    public bool IsActive => _isActive;

    public Visibility SectionVisibility => _isActive ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility => _isActive && Players.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string EmptyText => EMPTY_TEXT;

    /// <summary>True while a Local Session is current.</summary>
    public void SetActive(bool active)
    {
        if (_isActive == active)
        {
            return;
        }

        _isActive = active;
        if (active)
        {
            Refresh();
        }

        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(SectionVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    public void Refresh()
    {
        if (_playback is null || !_isActive)
        {
            return;
        }

        foreach (LocalPlayerSnapshot player in _playback.Players)
        {
            if (!_byAppId.ContainsKey(player.AppId))
            {
                LocalPlayerItemViewModel item = new(
                    player.AppId,
                    player.AppName,
                    _playback.IsPlayerEnabled(player.AppId),
                    OnPlayerToggled);
                _byAppId.Add(player.AppId, item);
                Players.Add(item);
            }
        }

        string? followedAppId = _playback.FollowedPlayer?.AppId;
        foreach (LocalPlayerItemViewModel item in Players)
        {
            item.IsFollowed = string.Equals(item.AppId, followedAppId, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private void OnPlaybackChanged() => _runOnUiThread(Refresh);

    private void OnPlayerToggled(LocalPlayerItemViewModel item, bool enabled) =>
        _playback?.SetPlayerEnabled(item.AppId, enabled);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>One player app on this computer, with its switch and whether it is the followed one.</summary>
public sealed class LocalPlayerItemViewModel : INotifyPropertyChanged
{
    private readonly Action<LocalPlayerItemViewModel, bool> _onToggled;

    private bool _isEnabled;
    private bool _isFollowed;

    public LocalPlayerItemViewModel(
        string appId,
        string appName,
        bool isEnabled,
        Action<LocalPlayerItemViewModel, bool> onToggled)
    {
        AppId = appId;
        AppName = appName;
        _isEnabled = isEnabled;
        _onToggled = onToggled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppId { get; }

    public string AppName { get; }

    /// <summary>Set by the switch; saves the change right away.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
            _onToggled(this, value);
        }
    }

    public bool IsFollowed
    {
        get => _isFollowed;
        internal set
        {
            if (_isFollowed == value)
            {
                return;
            }

            _isFollowed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FollowedVisibility));
        }
    }

    public Visibility FollowedVisibility => _isFollowed ? Visibility.Visible : Visibility.Collapsed;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
