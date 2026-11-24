using System;
using System.Collections.Generic;
using System.Linq;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Chooses the followed player: the enabled player with a track worth publishing that most recently
/// entered Playing. A switch away from a followed player waits until the newcomer has kept the lead for
/// <see cref="SWITCH_DEBOUNCE"/>, so a brief clip elsewhere does not steal the lyrics. When nothing is
/// playing the current player stays followed. Deterministic: the caller passes the time.
/// </summary>
public sealed class PlayerSelectionPolicy
{
    public static readonly TimeSpan SWITCH_DEBOUNCE = TimeSpan.FromSeconds(1);

    private string? _followedInstanceId;
    private string? _challengerInstanceId;
    private TimeSpan _challengerSince;

    public string? FollowedInstanceId => _followedInstanceId;

    public LocalPlayerSnapshot? Select(
        IReadOnlyList<LocalPlayerSnapshot> players,
        Func<string, bool> isEnabled,
        TimeSpan now)
    {
        List<LocalPlayerSnapshot> enabled = players.Where(p => isEnabled(p.AppId)).ToList();
        LocalPlayerSnapshot? current = enabled.FirstOrDefault(p => p.InstanceId == _followedInstanceId);
        LocalPlayerSnapshot? latestPlaying = MostRecentlyStarted(enabled, LocalPlayerStatus.Playing);

        if (current is null)
        {
            // Nothing followed yet, or the followed player left or was switched off: no reason to wait.
            LocalPlayerSnapshot? pick = latestPlaying ?? MostRecentlyStarted(enabled, LocalPlayerStatus.Paused);
            Follow(pick);
            return pick;
        }

        if (latestPlaying is null
            || latestPlaying.InstanceId == current.InstanceId
            || !Overtakes(latestPlaying, current))
        {
            ClearChallenger();
            return current;
        }

        if (_challengerInstanceId != latestPlaying.InstanceId)
        {
            _challengerInstanceId = latestPlaying.InstanceId;
            _challengerSince = now;
        }

        if (now - _challengerSince < SWITCH_DEBOUNCE)
        {
            return current;
        }

        Follow(latestPlaying);
        return latestPlaying;
    }

    public void Reset()
    {
        _followedInstanceId = null;
        ClearChallenger();
    }

    private static LocalPlayerSnapshot? MostRecentlyStarted(
        IEnumerable<LocalPlayerSnapshot> players,
        LocalPlayerStatus status) =>
        players
            .Where(p => p.Status == status && PublishWorthiness.IsWorthPublishing(p.Track))
            .OrderByDescending(p => p.LastStartedPlayingAt ?? TimeSpan.MinValue)
            .FirstOrDefault();

    private static bool Overtakes(LocalPlayerSnapshot challenger, LocalPlayerSnapshot current) =>
        current.Status != LocalPlayerStatus.Playing
        || !PublishWorthiness.IsWorthPublishing(current.Track)
        || (challenger.LastStartedPlayingAt ?? TimeSpan.MinValue) > (current.LastStartedPlayingAt ?? TimeSpan.MinValue);

    private void Follow(LocalPlayerSnapshot? player)
    {
        _followedInstanceId = player?.InstanceId;
        ClearChallenger();
    }

    private void ClearChallenger()
    {
        _challengerInstanceId = null;
        _challengerSince = TimeSpan.Zero;
    }
}
