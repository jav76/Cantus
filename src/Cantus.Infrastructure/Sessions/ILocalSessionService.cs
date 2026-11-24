using Cantus.Core.Models;

namespace Cantus.Infrastructure.Sessions;

public enum LocalSessionCreateStatus
{
    Created,
    InvalidDisplayName,
    LimitReached
}

/// <summary>The outcome of creating a Local Session. <see cref="Session"/> is set only when created.</summary>
public sealed record LocalSessionCreateResult(LocalSessionCreateStatus Status, UserSession? Session);

public interface ILocalSessionService
{
    /// <summary>
    /// Creates a Local Session after pruning idle ones. Refuses an invalid name, or when
    /// <see cref="LocalSessionOptions.MaxSessions"/> Local Sessions already exist.
    /// </summary>
    Task<LocalSessionCreateResult> CreateAsync(string? displayName, CancellationToken cancellationToken = default);

    /// <summary>Marks a Local Session as used now, so idle pruning keeps it.</summary>
    Task TouchAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes Local Sessions unused for longer than <see cref="LocalSessionOptions.IdleExpiryDays"/>.</summary>
    Task<int> PruneIdleAsync(CancellationToken cancellationToken = default);
}
