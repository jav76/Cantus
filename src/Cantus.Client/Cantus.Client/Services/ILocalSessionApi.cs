using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Logging;

namespace Cantus.Client.Services;

/// <summary>Outcome of <c>POST /api/auth/local</c>.</summary>
public enum LocalSessionStatus
{
    Created,

    /// <summary><c>400 invalid_display_name</c>.</summary>
    InvalidDisplayName,

    /// <summary><c>401 join_code_required</c>: the operator set a join code and none was sent.</summary>
    JoinCodeRequired,

    /// <summary><c>401 join_code_invalid</c>.</summary>
    JoinCodeInvalid,

    /// <summary><c>403 local_sessions_disabled</c>.</summary>
    Disabled,

    /// <summary><c>429</c>.</summary>
    RateLimited,

    /// <summary><c>503 local_session_limit</c>: the server holds its maximum number of Local Sessions.</summary>
    LimitReached,

    /// <summary>The server could not be reached or answered unexpectedly.</summary>
    Failed,
}

public sealed record LocalSessionResult(LocalSessionStatus Status, AuthorizedSessionPayload? Session = null);

/// <summary>Creates a Local Session: a Session for this computer's players, with no Spotify account.</summary>
[TraceLog]
public interface ILocalSessionApi
{
    Task<LocalSessionResult> CreateAsync(
        string displayName,
        [Redact] string? joinCode,
        CancellationToken cancellationToken = default);
}
