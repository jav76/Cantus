namespace Cantus.Server.Services;

/// <summary>
/// A pairing waiting for an owner to approve it. <see cref="ConnectionId"/> is the hub
/// connection that receives the Display token once the code is approved.
/// </summary>
public sealed record PendingDisplayPairing(
    string PairingId,
    string Code,
    string ConnectionId,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Holds pending Display pairings in memory. Codes are short, single-use and expire after
/// <see cref="DisplayPairingOptions.CodeLifetimeSeconds"/>.
/// </summary>
public interface IDisplayPairingService
{
    /// <summary>
    /// Starts a pairing for a hub connection. A connection has at most one pending pairing, so
    /// a new request replaces its previous code. Returns null when too many pairings are pending.
    /// </summary>
    PendingDisplayPairing? Request(string connectionId);

    /// <summary>
    /// Removes and returns the pending pairing for a code, so each code is approved at most once.
    /// Matching ignores case, spaces and hyphens. Returns null for an unknown or expired code.
    /// </summary>
    PendingDisplayPairing? Claim(string code);

    /// <summary>Drops the connection's pending pairing, for example when it disconnects.</summary>
    void Cancel(string connectionId);
}
