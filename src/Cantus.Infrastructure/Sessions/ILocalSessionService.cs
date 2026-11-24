using Cantus.Core.Models;

namespace Cantus.Infrastructure.Sessions;

public enum LocalSessionCreateStatus
{
    Created,
    InvalidDisplayName,
    LimitReached
}

/// <summary>
/// The outcome of creating a Local Session. <see cref="Session"/> is set only when created;
/// <see cref="Pruned"/> lists what the idle prune before it deleted.
/// </summary>
public sealed record LocalSessionCreateResult(
    LocalSessionCreateStatus Status,
    UserSession? Session,
    LocalSessionPruneResult Pruned);

/// <summary>The Local Sessions a prune deleted, and the Displays the database deleted with them.</summary>
public sealed record LocalSessionPruneResult(IReadOnlyList<string> SessionIds, IReadOnlyList<string> DisplayIds)
{
    public static LocalSessionPruneResult None { get; } = new([], []);
}

public interface ILocalSessionService
{
    /// <summary>
    /// Creates a Local Session after pruning idle ones. Refuses an invalid name, or when
    /// <see cref="LocalSessionOptions.MaxSessions"/> Local Sessions already exist.
    /// </summary>
    Task<LocalSessionCreateResult> CreateAsync(string? displayName, CancellationToken cancellationToken = default);

    /// <summary>Marks a Local Session as used now, so idle pruning keeps it.</summary>
    Task TouchAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes Local Sessions unused for longer than <see cref="LocalSessionOptions.IdleExpiryDays"/>.
    /// Returns what it deleted, so the caller can drop the live state held for each.
    /// </summary>
    Task<LocalSessionPruneResult> PruneIdleAsync(CancellationToken cancellationToken = default);
}
