using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// The Linux source: MPRIS players on the D-Bus session bus. Players are found with <c>ListNames</c>
/// and followed through <c>NameOwnerChanged</c>; their state comes from <c>GetAll</c>,
/// <c>PropertiesChanged</c> and <c>Seeked</c>. Because MPRIS never signals position changes, the
/// position is read again on status, track or rate changes and every five seconds while playing.
/// </summary>
internal sealed class MprisPlayerSource : ILocalPlayerSource
{
    public static readonly TimeSpan POSITION_REFRESH_INTERVAL = TimeSpan.FromSeconds(5);

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<MprisPlayerSource>();

    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, MprisPlayerState> _players = new(StringComparer.Ordinal);

    // Bumped on every NameOwnerChanged for a name, so an add that was still reading the player when
    // its owner changed does not store a result for the old owner.
    private readonly Dictionary<string, int> _ownerGenerations = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _subscriptions = new();

    private MprisBus? _bus;
    private ITimer? _refreshTimer;
    private IReadOnlyList<LocalPlayerSnapshot> _snapshots = [];
    private bool _isSupported = MprisBus.HasSessionBusAddress;
    private bool _started;
    private bool _disposed;

    public MprisPlayerSource(TimeProvider clock)
    {
        _clock = clock;
    }

    public bool IsSupported => Volatile.Read(ref _isSupported);

    public IReadOnlyList<LocalPlayerSnapshot> Players => Volatile.Read(ref _snapshots);

    public event Action? PlayersChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started || _disposed || !_isSupported)
            {
                return;
            }

            _started = true;
        }

        MprisBus bus = new();
        try
        {
            await bus.ConnectAsync();
            _bus = bus;

            // Subscribe before listing so a player that appears in between is not missed.
            _subscriptions.Add(await bus.WatchNameOwnerChangedAsync(OnNameOwnerChanged));
            _subscriptions.Add(await bus.WatchPropertiesChangedAsync(OnPropertiesChanged));
            _subscriptions.Add(await bus.WatchSeekedAsync(OnSeeked));

            string[] names = await bus.ListNamesAsync();
            foreach (string name in names.Where(LocalPlayerIdentity.IsMprisBusName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AddPlayerAsync(name, owner: null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The D-Bus session bus is unavailable; local players cannot be read");
            Volatile.Write(ref _isSupported, false);
            DisposeBus();
            return;
        }

        _refreshTimer = _clock.CreateTimer(
            _ => _ = RefreshPlayingPositionsAsync(),
            null,
            POSITION_REFRESH_INTERVAL,
            POSITION_REFRESH_INTERVAL);
        _ = WatchForDisconnectAsync(bus);
        _logger.LogInformation("Watching MPRIS players; found {Count}", Players.Count);
    }

    public async Task<bool> SendAsync(
        string instanceId,
        LocalPlayerCommand command,
        long? positionMs = null,
        CancellationToken cancellationToken = default)
    {
        MprisBus? bus = _bus;
        string? trackPath;
        lock (_gate)
        {
            if (bus is null || !_players.TryGetValue(instanceId, out MprisPlayerState? state))
            {
                return false;
            }

            trackPath = state.TrackObjectPath;
        }

        try
        {
            switch (command)
            {
                case LocalPlayerCommand.Seek:
                    if (positionMs is not long targetMs || string.IsNullOrEmpty(trackPath) || !trackPath.StartsWith('/'))
                    {
                        return false;
                    }

                    await bus.SetPositionAsync(instanceId, trackPath, Math.Max(0, targetMs) * 1000);
                    return true;

                default:
                    await bus.CallPlayerMethodAsync(instanceId, ToMember(command));
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MPRIS command {Command} to {Player} failed", command, instanceId);
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        DisposeBus();
        return ValueTask.CompletedTask;
    }

    private static string ToMember(LocalPlayerCommand command) => command switch
    {
        LocalPlayerCommand.PlayPause => "PlayPause",
        LocalPlayerCommand.Play => "Play",
        LocalPlayerCommand.Pause => "Pause",
        LocalPlayerCommand.Next => "Next",
        LocalPlayerCommand.Previous => "Previous",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    private async Task AddPlayerAsync(string busName, string? owner)
    {
        MprisBus? bus = _bus;
        if (bus is null)
        {
            return;
        }

        int generation;
        lock (_gate)
        {
            generation = OwnerGenerationLocked(busName);
        }

        try
        {
            owner ??= await bus.GetNameOwnerAsync(busName);
            Dictionary<string, object?> root = await TryGetAllAsync(bus, busName, MprisBus.ROOT_INTERFACE);
            Dictionary<string, object?> player = await bus.GetAllPropertiesAsync(busName, MprisBus.PLAYER_INTERFACE);

            MprisPlayerState state = new(busName, owner);
            state.ApplyRootProperties(root);
            state.ApplyPlayerProperties(player, Now);

            lock (_gate)
            {
                if (generation != OwnerGenerationLocked(busName))
                {
                    _logger.LogDebug("MPRIS player {Player} changed owner while it was being read", busName);
                    return;
                }

                _players[busName] = state;
                PublishSnapshotsLocked();
            }

            _logger.LogDebug("MPRIS player {Player} ({AppId}) is {Status}", busName, state.AppId, state.Status);
        }
        catch (Exception ex)
        {
            // The player left between being listed and answering.
            _logger.LogDebug(ex, "Could not read MPRIS player {Player}", busName);
            return;
        }

        RaisePlayersChanged();
    }

    private static async Task<Dictionary<string, object?>> TryGetAllAsync(MprisBus bus, string busName, string interfaceName)
    {
        try
        {
            return await bus.GetAllPropertiesAsync(busName, interfaceName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Player} has no readable {Interface}", busName, interfaceName);
            return new Dictionary<string, object?>();
        }
    }

    private void OnNameOwnerChanged(string name, string oldOwner, string newOwner)
    {
        if (!LocalPlayerIdentity.IsMprisBusName(name))
        {
            return;
        }

        bool removed;
        lock (_gate)
        {
            _ownerGenerations[name] = OwnerGenerationLocked(name) + 1;
            removed = _players.Remove(name);
            if (removed)
            {
                PublishSnapshotsLocked();
            }
        }

        if (removed)
        {
            _logger.LogDebug("MPRIS player {Player} left", name);
            RaisePlayersChanged();
        }

        if (!string.IsNullOrEmpty(newOwner))
        {
            _ = AddPlayerAsync(name, newOwner);
        }
    }

    private void OnPropertiesChanged(
        string sender,
        string interfaceName,
        Dictionary<string, object?> changed,
        string[] invalidated)
    {
        bool isPlayer = interfaceName == MprisBus.PLAYER_INTERFACE;
        if (!isPlayer && interfaceName != MprisBus.ROOT_INTERFACE)
        {
            return;
        }

        _logger.LogTrace(
            "PropertiesChanged from {Sender} on {Interface}: {Changed}",
            sender,
            interfaceName,
            string.Join(", ", changed.Keys.Concat(invalidated)));

        List<string> needPosition = new();
        List<string> needRefresh = new();
        lock (_gate)
        {
            List<MprisPlayerState> states = StatesOwnedByLocked(sender);
            if (states.Count == 0)
            {
                return;
            }

            TimeSpan now = Now;
            foreach (MprisPlayerState state in states)
            {
                if (isPlayer)
                {
                    if (state.ApplyPlayerProperties(changed, now))
                    {
                        needPosition.Add(state.BusName);
                    }
                }
                else
                {
                    state.ApplyRootProperties(changed);
                }

                if (invalidated.Length > 0)
                {
                    needRefresh.Add(state.BusName);
                }
            }

            PublishSnapshotsLocked();
        }

        RaisePlayersChanged();

        foreach (string busName in needPosition)
        {
            _ = ReadPositionAsync(busName);
        }

        foreach (string busName in needRefresh)
        {
            _ = RefreshPropertiesAsync(busName, interfaceName);
        }
    }

    private void OnSeeked(string sender, long positionMicroseconds)
    {
        _logger.LogTrace("Seeked from {Sender} to {Position} us", sender, positionMicroseconds);
        lock (_gate)
        {
            List<MprisPlayerState> states = StatesOwnedByLocked(sender);
            if (states.Count == 0)
            {
                return;
            }

            TimeSpan now = Now;
            foreach (MprisPlayerState state in states)
            {
                state.ApplySeeked(positionMicroseconds, now);
            }

            PublishSnapshotsLocked();
        }

        RaisePlayersChanged();
    }

    private async Task RefreshPlayingPositionsAsync()
    {
        List<string> playing;
        lock (_gate)
        {
            playing = _players.Values
                .Where(p => p.Status == LocalPlayerStatus.Playing)
                .Select(p => p.BusName)
                .ToList();
        }

        foreach (string busName in playing)
        {
            await ReadPositionAsync(busName);
        }
    }

    private async Task ReadPositionAsync(string busName)
    {
        MprisBus? bus = _bus;
        if (bus is null)
        {
            return;
        }

        MprisPlayerState? before;
        int timeline;
        lock (_gate)
        {
            if (!_players.TryGetValue(busName, out before))
            {
                return;
            }

            timeline = before.TimelineGeneration;
        }

        long microseconds;
        try
        {
            if (MprisMetadataParser.ToInt64(
                    await bus.GetPlayerPropertyAsync(busName, MprisPlayerState.PROPERTY_POSITION)) is not long value)
            {
                return;
            }

            microseconds = value;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the position of {Player}", busName);
            if (MprisBus.IsNameGone(ex))
            {
                RemovePlayer(busName, before);
            }

            return;
        }

        lock (_gate)
        {
            // Dropped when the player was replaced, or its track, status, rate or position changed while
            // the read was in flight: the answer may describe the old timeline.
            if (!_players.TryGetValue(busName, out MprisPlayerState? state)
                || state != before
                || state.TimelineGeneration != timeline)
            {
                return;
            }

            state.SetPosition(microseconds, Now);
            PublishSnapshotsLocked();
        }

        RaisePlayersChanged();
    }

    private async Task RefreshPropertiesAsync(string busName, string interfaceName)
    {
        MprisBus? bus = _bus;
        if (bus is null)
        {
            return;
        }

        Dictionary<string, object?> properties;
        try
        {
            properties = await bus.GetAllPropertiesAsync(busName, interfaceName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not refresh {Player}", busName);
            return;
        }

        lock (_gate)
        {
            if (!_players.TryGetValue(busName, out MprisPlayerState? state))
            {
                return;
            }

            if (interfaceName == MprisBus.PLAYER_INTERFACE)
            {
                state.ApplyPlayerProperties(properties, Now);
            }
            else
            {
                state.ApplyRootProperties(properties);
            }

            PublishSnapshotsLocked();
        }

        RaisePlayersChanged();
    }

    private async Task WatchForDisconnectAsync(MprisBus bus)
    {
        try
        {
            Exception? reason = await bus.WhenDisconnectedAsync();
            if (_disposed)
            {
                return;
            }

            _logger.LogWarning(reason, "The D-Bus session bus connection closed; local players are no longer read");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stopped watching the D-Bus connection");
        }

        lock (_gate)
        {
            _players.Clear();
            PublishSnapshotsLocked();
        }

        RaisePlayersChanged();
    }

    private void RemovePlayer(string busName, MprisPlayerState expected)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(busName, out MprisPlayerState? state) || state != expected)
            {
                return;
            }

            _players.Remove(busName);
            PublishSnapshotsLocked();
        }

        _logger.LogDebug("MPRIS player {Player} no longer has an owner; dropped it", busName);
        RaisePlayersChanged();
    }

    private int OwnerGenerationLocked(string busName) =>
        _ownerGenerations.TryGetValue(busName, out int generation) ? generation : 0;

    private List<MprisPlayerState> StatesOwnedByLocked(string owner) =>
        _players.Values.Where(p => p.Owner == owner).ToList();

    private void PublishSnapshotsLocked()
    {
        Volatile.Write(
            ref _snapshots,
            _players.Values.OrderBy(p => p.BusName, StringComparer.Ordinal).Select(p => p.ToSnapshot()).ToList());
    }

    private void RaisePlayersChanged()
    {
        try
        {
            PlayersChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A local player listener failed");
        }
    }

    private TimeSpan Now => MonotonicClock.Now(_clock);

    private void DisposeBus()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _bus?.Dispose();
        _bus = null;
    }
}
