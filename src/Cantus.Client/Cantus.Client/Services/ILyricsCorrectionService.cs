using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Logging;

namespace Cantus.Client.Services;

public enum LyricsCorrectionStatus
{
    Succeeded = 0,

    /// <summary>The server does not recognize this client's Session (401).</summary>
    SignedOut = 1,

    /// <summary>The caller holds a Display token, which is view-only (403).</summary>
    NotAllowed = 2,

    /// <summary>The track is no longer playing, or the provider no longer has the candidate (404).</summary>
    NotFound = 3,

    /// <summary>A lyrics provider could not be reached; nothing was changed (502).</summary>
    ProviderUnavailable = 4,

    /// <summary>The server could not be reached or answered with anything else.</summary>
    Failed = 5
}

public sealed record LyricsCandidatesResult(LyricsCorrectionStatus Status, LyricsCandidatesPayload? Candidates);

public sealed record LyricsOverrideResult(LyricsCorrectionStatus Status, LyricsPayload? Lyrics);

/// <summary>
/// The owner's lyrics correction endpoints: listing candidates for the current
/// track and applying an override. Both require a Session.
/// </summary>
[TraceLog]
public interface ILyricsCorrectionService
{
    Task<LyricsCandidatesResult> GetCandidatesAsync(string trackId, CancellationToken cancellationToken = default);

    Task<LyricsOverrideResult> ApplyOverrideAsync(
        string trackId,
        LyricsOverrideRequestPayload request,
        CancellationToken cancellationToken = default);
}
