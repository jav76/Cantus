using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;

namespace Cantus.Client.Tests.LocalPlayback;

/// <summary>An <see cref="ILocalPlayback"/> the view model tests drive by hand; it records what was asked of it.</summary>
internal sealed class FakeLocalPlayback : ILocalPlayback
{
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<LocalSessionResult> _results = new();

    public bool IsSupported { get; set; } = true;

    public string? JoinCodePrefill { get; set; }

    public IReadOnlyList<LocalPlayerSnapshot> Players { get; private set; } = [];

    public LocalPlayerSnapshot? FollowedPlayer { get; private set; }

    public List<string?> CreatedWithJoinCodes { get; } = new();

    public List<(string AppId, bool Enabled)> Switches { get; } = new();

    public int SignedOutCount { get; private set; }

    /// <summary>Runs when a creation starts, so a test can record the order of calls.</summary>
    public Action? OnCreate { get; set; }

    public event Action? AvailabilityChanged;

    public event Action? PlayersChanged;

    public event Action? FollowedPlayerChanged;

    public void EnqueueResults(params LocalSessionStatus[] statuses)
    {
        foreach (LocalSessionStatus status in statuses)
        {
            _results.Enqueue(status == LocalSessionStatus.Created
                ? new LocalSessionResult(
                    status,
                    new AuthorizedSessionPayload { Id = "local-1", Kind = AuthorizedSessionPayload.KIND_LOCAL })
                : new LocalSessionResult(status));
        }
    }

    public void RaiseAvailabilityChanged() => AvailabilityChanged?.Invoke();

    public void SetPlayers(params LocalPlayerSnapshot[] players)
    {
        Players = players;
        PlayersChanged?.Invoke();
    }

    public void SetFollowed(LocalPlayerSnapshot? player)
    {
        FollowedPlayer = player;
        FollowedPlayerChanged?.Invoke();
    }

    public bool IsPlayerEnabled(string appId) => !_enabled.TryGetValue(appId, out bool enabled) || enabled;

    public void SetPlayerEnabled(string appId, bool enabled)
    {
        _enabled[appId] = enabled;
        Switches.Add((appId, enabled));
    }

    public Task<LocalSessionResult> CreateSessionAsync(string? joinCode, CancellationToken cancellationToken = default)
    {
        OnCreate?.Invoke();
        CreatedWithJoinCodes.Add(joinCode);
        LocalSessionResult result = _results.Count > 0
            ? _results.Dequeue()
            : new LocalSessionResult(LocalSessionStatus.Failed);
        return Task.FromResult(result);
    }

    public void NotifySignedOut() => SignedOutCount++;
}
