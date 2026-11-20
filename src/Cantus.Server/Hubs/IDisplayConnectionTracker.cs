using Microsoft.AspNetCore.SignalR;

namespace Cantus.Server.Hubs;

/// <summary>
/// Tracks the live hub connections of each Display so revoking a Display can close them.
/// A connection's credential is resolved only when it opens, so without this a revoked
/// Display would keep receiving its owner's playback until it reconnected.
/// </summary>
public interface IDisplayConnectionTracker
{
    /// <summary>
    /// Tracks a Display connection. Returns false when the Display was revoked moments ago,
    /// which happens when its token was validated just before the revocation; the caller
    /// must then close the connection itself.
    /// </summary>
    bool Add(string displayId, HubCallerContext connection);

    void Remove(string displayId, string connectionId);

    IReadOnlyList<string> GetConnectionIds(string displayId);

    /// <summary>
    /// Sends <see cref="IPlaybackClient.ReceiveDisplayRevoked"/> to each of the Display's
    /// connections, then aborts them. Returns how many connections were closed.
    /// </summary>
    Task<int> DisconnectAsync(string displayId);
}
