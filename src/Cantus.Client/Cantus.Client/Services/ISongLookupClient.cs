using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Logging;

namespace Cantus.Client.Services;

/// <summary>How a song lookup HTTP call ended. Client-only: it never crosses the wire.</summary>
public enum SongLookupCallStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    RateLimited,
    Failed
}

/// <param name="Response">The parsed body; set only when <paramref name="Status"/> is Success.</param>
/// <param name="ErrorMessage">The server's message for a 400, when it sent one.</param>
public sealed record SongLookupCallResult(
    SongLookupCallStatus Status,
    SongLookupResponsePayload? Response = null,
    string? ErrorMessage = null);

/// <param name="Response">The parsed body; set only when <paramref name="Status"/> is Success.</param>
/// <param name="ErrorMessage">The server's message for a 400, when it sent one.</param>
public sealed record SongPlayCallResult(
    SongLookupCallStatus Status,
    SongLookupPlayResponsePayload? Response = null,
    string? ErrorMessage = null);

/// <summary>
/// REST client for the owner-only song lookup endpoints. Parameters are not traced: a
/// fragment is something the user typed and a guess carries lyric text.
/// </summary>
[TraceLog(false, false)]
public interface ISongLookupClient
{
    /// <param name="scope">One of the <see cref="SongLookupSearchRequestPayload"/> SCOPE_* values.</param>
    Task<SongLookupCallResult> SearchAsync(
        string fragment,
        string scope,
        CancellationToken cancellationToken = default);

    Task<SongPlayCallResult> PlayAsync(
        SongLookupPlayRequestPayload request,
        CancellationToken cancellationToken = default);
}
