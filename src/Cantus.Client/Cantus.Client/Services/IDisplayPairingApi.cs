using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Logging;

namespace Cantus.Client.Services;

/// <summary>Outcome of asking the server for a pairing code.</summary>
public enum DisplayPairingRequestStatus
{
    Created,
    RateLimited,
    Unavailable,
    Failed,
}

public sealed record DisplayPairingRequestResult(
    DisplayPairingRequestStatus Status,
    DisplayPairingCodePayload? Pairing = null);

/// <summary>The REST side of pairing a Display: <c>POST /api/displays/pairings</c>.</summary>
[TraceLog]
public interface IDisplayPairingApi
{
    /// <summary>Starts a pairing for a live, unauthenticated hub connection.</summary>
    Task<DisplayPairingRequestResult> RequestPairingAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
