using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

/// <summary>
/// Creates, validates and manages Displays: screens paired to an owner's Session that
/// hold a view-only Display token.
/// </summary>
[TraceLog]
public interface IDisplayService
{
    /// <summary>
    /// Creates a Display for an existing owner Session. The returned token is shown once
    /// and is not stored in plain text.
    /// </summary>
    Task<CreatedDisplay> CreateAsync(
        [Redact] string ownerSessionId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a Display token to its Display, or null when the token is unknown or revoked.
    /// Records when the Display was last seen.
    /// </summary>
    Task<DisplayInfo?> ValidateAsync([Redact] string token, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisplayInfo>> ListAsync(
        [Redact] string ownerSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames one of the owner's Displays. Returns false when the owner has no Display with that id.
    /// </summary>
    Task<bool> RenameAsync(
        [Redact] string ownerSessionId,
        string displayId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one of the owner's Displays so its token stops working. The owner's Session is
    /// untouched. Returns false when the owner has no Display with that id.
    /// </summary>
    Task<bool> RevokeAsync(
        [Redact] string ownerSessionId,
        string displayId,
        CancellationToken cancellationToken = default);
}
