using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// The media players the operating system reports on this computer (MPRIS on Linux).
/// <see cref="PlayersChanged"/> may be raised on any thread.
/// </summary>
public interface ILocalPlayerSource : IAsyncDisposable
{
    /// <summary>False when this platform has no source, or the source could not connect.</summary>
    bool IsSupported { get; }

    IReadOnlyList<LocalPlayerSnapshot> Players { get; }

    event Action? PlayersChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a transport command to one player. Returns false when the player refused or vanished.</summary>
    Task<bool> SendAsync(
        string instanceId,
        LocalPlayerCommand command,
        long? positionMs = null,
        CancellationToken cancellationToken = default);
}
