using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// What the UI needs from local player publishing: whether this computer's players can be read, the
/// players and the followed one, the per-player switches, and creating a Local Session when the owner
/// picks it. Events may be raised on any thread.
/// </summary>
public interface ILocalPlayback
{
    /// <summary>True once the platform source has started and can read players.</summary>
    bool IsSupported { get; }

    /// <summary>A join code to prefill the join code field with, or null.</summary>
    string? JoinCodePrefill { get; }

    IReadOnlyList<LocalPlayerSnapshot> Players { get; }

    /// <summary>The player being published, or null when none is or publishing is stopped.</summary>
    LocalPlayerSnapshot? FollowedPlayer { get; }

    /// <summary>Raised when <see cref="IsSupported"/> becomes true.</summary>
    event Action? AvailabilityChanged;

    event Action? PlayersChanged;

    event Action? FollowedPlayerChanged;

    bool IsPlayerEnabled(string appId);

    /// <summary>Saves the player's switch; the followed player is chosen again right away.</summary>
    void SetPlayerEnabled(string appId, bool enabled);

    /// <summary>
    /// Creates a Local Session for this computer and, when the server accepts, connects with it.
    /// <paramref name="beforeConnect"/> runs only after the server accepted, before connecting, so a
    /// Session already held can be signed out without losing it on a refusal.
    /// </summary>
    Task<LocalSessionResult> CreateSessionAsync(
        string? joinCode,
        Func<Task>? beforeConnect = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stops publishing because the owner signed out.</summary>
    void NotifySignedOut();
}
