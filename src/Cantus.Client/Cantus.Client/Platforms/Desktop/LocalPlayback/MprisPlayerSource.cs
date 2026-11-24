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
/// Calls go to each player's unique connection name, so the answer always comes from the owner the
/// state belongs to.
/// </summary>
internal sealed class MprisPlayerSource : ILocalPlayerSource
{
    public static readonly TimeSpan POSITION_REFRESH_INTERVAL = TimeSpan.FromSeconds(5);

    /// <summary>How often a <c>GetAll</c> is repeated when signals from the player overtook it.</summary>
    public const int MAX_SETTLE_ATTEMPTS = 3;

    private const int INITIAL_ADD_RETRY_DELAY_SECONDS = 5;
    private const int MAX_ADD_RETRY_DOUBLINGS = 3;
    private const int MAX_ADD_RETRY_DELAY_SECONDS = 30;

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<MprisPlayerSource>();

    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, MprisPlayerState> _players = new(StringComparer.Ordinal);

    // Bumped on every NameOwnerChanged for a name, so an add that was still reading the player when
    // its owner changed does not store a result for the old owner.
    private readonly Dictionary<string, int> _ownerGenerations = new(StringComparer.Ordinal);

    // Signals received per unique sender. A GetAll whose count moved while it was in flight may have
    // been overtaken by a signal it does not reflect (or, while adding, one that found no state yet).
    private readonly Dictionary<string, int> _signalCounts = new(StringComparer.Ordinal);

    // Players whose 5 s position read is still in flight, so a hung player does not pile up reads.
    private readonly HashSet<string> _refreshingPositions = new(StringComparer.Ordinal);

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
            await bus.ConnectAsync().ConfigureAwait(false);
            _bus = bus;

            // Subscribe before listing so a player that appears in between is not missed.
            _subscriptions.Add(await bus.WatchNameOwnerChangedAsync(OnNameOwnerChanged).ConfigureAwait(false));
            _subscriptions.Add(await bus.WatchPropertiesChangedAsync(OnPropertiesChanged).ConfigureAwait(false));
            _subscriptions.Add(await bus.WatchSeekedAsync(OnSeeked).ConfigureAwait(false));

            string[] names = await bus.ListNamesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Read in parallel so one slow player does not hold up the others.
            await Task.WhenAll(names
                .Where(LocalPlayerIdentity.IsMprisBusName)
                .Select(name => AddPlayerAsync(name, owner: null))).ConfigureAwait(false);
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
        string owner;
        lock (_gate)
        {
            if (bus is null || !_players.TryGetValue(instanceId, out MprisPlayerState? state))
            {
                return false;
            }

            trackPath = state.TrackObjectPath;
            owner = state.Owner;
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

                    long targetMicroseconds = Math.Max(0, targetMs) * TimeSpan.MicrosecondsPerMillisecond;
                    await bus.SetPositionAsync(owner, trackPath, targetMicroseconds).ConfigureAwait(false);
                    return true;

                default:
                    await bus.CallPlayerMethodAsync(owner, ToMember(command)).ConfigureAwait(false);
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

    private Task AddPlayerAsync(string busName, string? owner)
    {
        int generation;
        lock (_gate)
        {
            generation = OwnerGenerationLocked(busName);
        }

        return TryAddPlayerAsync(busName, owner, generation, timeouts: 0);
    }

    private async Task TryAddPlayerAsync(string busName, string? owner, int generation, int timeouts)
    {
        MprisBus? bus = _bus;
        if (bus is null)
        {
            return;
        }

        try
        {
            owner ??= await bus.GetNameOwnerAsync(busName).ConfigureAwait(false);
            await ReadAndStorePlayerAsync(bus, busName, owner, generation).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            // Busy, not gone: a player that left changes the generation, which ends the retries. They
            // run detached so startup is not held up by a player that never answers.
            TimeSpan delay = AddRetryDelay(timeouts);
            _logger.LogDebug(ex, "MPRIS player {Player} did not answer; trying again in {Delay}", busName, delay);
            _ = RetryAddPlayerAsync(busName, owner, generation, timeouts + 1, delay);
        }
        catch (Exception ex)
        {
            // The player left between being listed and answering.
            _logger.LogDebug(ex, "Could not read MPRIS player {Player}", busName);
        }
    }

    private async Task RetryAddPlayerAsync(string busName, string? owner, int generation, int timeouts, TimeSpan delay)
    {
        await Task.Delay(delay, _clock).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || generation != OwnerGenerationLocked(busName))
            {
                return;
            }
        }

        await TryAddPlayerAsync(busName, owner, generation, timeouts).ConfigureAwait(false);
    }

    /// <summary>5, 10, 20, then every 30 seconds.</summary>
    private static TimeSpan AddRetryDelay(int previousTimeouts) =>
        TimeSpan.FromSeconds(Math.Min(
            MAX_ADD_RETRY_DELAY_SECONDS,
            INITIAL_ADD_RETRY_DELAY_SECONDS << Math.Min(previousTimeouts, MAX_ADD_RETRY_DOUBLINGS)));

    private async Task ReadAndStorePlayerAsync(MprisBus bus, string busName, string owner, int generation)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            int signals;
            lock (_gate)
            {
                signals = SignalCountLocked(owner);
            }

            Dictionary<string, object?> root = await TryGetAllAsync(bus, owner, MprisBus.ROOT_INTERFACE).ConfigureAwait(false);
            Dictionary<string, object?> player = await bus
                .GetAllPropertiesAsync(owner, MprisBus.PLAYER_INTERFACE, MprisBus.SETUP_CALL_TIMEOUT)
                .ConfigureAwait(false);

            MprisPlayerState state = new(busName, owner);
            state.ApplyRootProperties(root);
            state.ApplyPlayerProperties(player, Now);

            bool overtaken;
            lock (_gate)
            {
                if (generation != OwnerGenerationLocked(busName))
                {
                    _logger.LogDebug("MPRIS player {Player} changed owner while it was being read", busName);
                    return;
                }

                // Signals that arrived before the state was stored found nothing to update.
                overtaken = SignalCountLocked(owner) != signals;
                if (overtaken && attempt < MAX_SETTLE_ATTEMPTS)
                {
                    continue;
                }

                _players[busName] = state;
                PublishSnapshotsLocked();
            }

            _logger.LogDebug("MPRIS player {Player} ({AppId}) is {Status}", busName, state.AppId, state.Status);
            RaisePlayersChanged();
            if (overtaken)
            {
                _ = RefreshPropertiesAsync(busName, MprisBus.PLAYER_INTERFACE, isFollowUp: true);
                _ = ReadPositionAsync(busName);
            }

            return;
        }
    }

    private static async Task<Dictionary<string, object?>> TryGetAllAsync(MprisBus bus, string destination, string interfaceName)
    {
        try
        {
            return await bus.GetAllPropertiesAsync(destination, interfaceName, MprisBus.SETUP_CALL_TIMEOUT).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            _logger.LogDebug(ex, "{Player} has no readable {Interface}", destination, interfaceName);
            return new Dictionary<string, object?>();
        }
    }

    private void OnNameOwnerChanged(string name, string oldOwner, string newOwner)
    {
        if (string.IsNullOrEmpty(newOwner) && !string.IsNullOrEmpty(oldOwner))
        {
            lock (_gate)
            {
                _signalCounts.Remove(oldOwner);
            }
        }

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
            CountSignalLocked(sender);
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
            CountSignalLocked(sender);
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
                .Where(p => p.Status == LocalPlayerStatus.Playing && !_refreshingPositions.Contains(p.BusName))
                .Select(p => p.BusName)
                .ToList();
            _refreshingPositions.UnionWith(playing);
        }

        await Task.WhenAll(playing.Select(RefreshPositionAsync)).ConfigureAwait(false);
    }

    private async Task RefreshPositionAsync(string busName)
    {
        try
        {
            await ReadPositionAsync(busName).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _refreshingPositions.Remove(busName);
            }
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
                    await bus.GetPlayerPropertyAsync(before.Owner, MprisPlayerState.PROPERTY_POSITION).ConfigureAwait(false))
                is not long value)
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

    /// <param name="isFollowUp">
    /// True for the one extra read made when signals kept overtaking the previous one, so a player that
    /// signals non-stop does not chain reads forever.
    /// </param>
    private async Task RefreshPropertiesAsync(string busName, string interfaceName, bool isFollowUp = false)
    {
        MprisBus? bus = _bus;
        if (bus is null)
        {
            return;
        }

        bool needsPosition = false;
        bool overtaken;
        int attempt = 0;
        while (true)
        {
            attempt++;
            MprisPlayerState? target;
            int signals;
            lock (_gate)
            {
                if (!_players.TryGetValue(busName, out target))
                {
                    return;
                }

                signals = SignalCountLocked(target.Owner);
            }

            Dictionary<string, object?> properties;
            try
            {
                properties = await bus.GetAllPropertiesAsync(target.Owner, interfaceName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not refresh {Player}", busName);
                return;
            }

            lock (_gate)
            {
                if (!_players.TryGetValue(busName, out MprisPlayerState? state) || state != target)
                {
                    return;
                }

                // A signal applied while the call was in flight may be newer than this answer.
                overtaken = SignalCountLocked(state.Owner) != signals;
                if (overtaken && attempt < MAX_SETTLE_ATTEMPTS)
                {
                    continue;
                }

                if (interfaceName == MprisBus.PLAYER_INTERFACE)
                {
                    needsPosition = state.ApplyPlayerProperties(properties, Now);
                }
                else
                {
                    state.ApplyRootProperties(properties);
                }

                PublishSnapshotsLocked();
            }

            break;
        }

        RaisePlayersChanged();
        if (overtaken && !isFollowUp)
        {
            _ = RefreshPropertiesAsync(busName, interfaceName, isFollowUp: true);
            needsPosition |= interfaceName == MprisBus.PLAYER_INTERFACE;
        }

        if (needsPosition)
        {
            _ = ReadPositionAsync(busName);
        }
    }

    private async Task WatchForDisconnectAsync(MprisBus bus)
    {
        try
        {
            Exception? reason = await bus.WhenDisconnectedAsync().ConfigureAwait(false);
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

    private int SignalCountLocked(string owner) =>
        _signalCounts.TryGetValue(owner, out int count) ? count : 0;

    private void CountSignalLocked(string sender) =>
        _signalCounts[sender] = unchecked(SignalCountLocked(sender) + 1);

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
