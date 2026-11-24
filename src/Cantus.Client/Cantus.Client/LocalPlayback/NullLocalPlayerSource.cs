using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cantus.Client.LocalPlayback;

/// <summary>The source on platforms that cannot read other apps' playback.</summary>
public sealed class NullLocalPlayerSource : ILocalPlayerSource
{
    public bool IsSupported => false;

    public IReadOnlyList<LocalPlayerSnapshot> Players => [];

    public event Action? PlayersChanged
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> SendAsync(
        string instanceId,
        LocalPlayerCommand command,
        long? positionMs = null,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
