using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Logging;

namespace Cantus.Client.Services;

/// <summary>Outcome of an owner's Display management request.</summary>
public enum DisplayManagementStatus
{
    Succeeded,

    /// <summary><c>400</c>: the name is empty or too long.</summary>
    InvalidName,

    /// <summary><c>404</c>: the code is unknown, used or expired, or the owner has no such Display.</summary>
    NotFound,

    /// <summary><c>401</c>: no Session credential.</summary>
    NotSignedIn,

    /// <summary><c>403</c>: the caller holds a Display token, which is view-only.</summary>
    Forbidden,

    /// <summary><c>429</c>: the approve rate limit was reached.</summary>
    RateLimited,

    /// <summary>The server could not be reached or answered unexpectedly.</summary>
    Failed,
}

public sealed record DisplayApprovalResult(DisplayManagementStatus Status, DisplayInfoPayload? Display = null);

public sealed record DisplayListResult(
    DisplayManagementStatus Status,
    IReadOnlyList<DisplayInfoPayload>? Displays = null);

/// <summary>The owner's side of the Display endpoints: approve a code, list, rename and revoke.</summary>
[TraceLog]
public interface IDisplayManagementApi
{
    /// <summary><c>POST /api/displays/pairings/approve</c>.</summary>
    Task<DisplayApprovalResult> ApproveAsync(
        [Redact] string code,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/displays</c>.</summary>
    Task<DisplayListResult> ListAsync(CancellationToken cancellationToken = default);

    /// <summary><c>PATCH /api/displays/{displayId}</c>.</summary>
    Task<DisplayManagementStatus> RenameAsync(
        string displayId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary><c>DELETE /api/displays/{displayId}</c>.</summary>
    Task<DisplayManagementStatus> RevokeAsync(string displayId, CancellationToken cancellationToken = default);
}
