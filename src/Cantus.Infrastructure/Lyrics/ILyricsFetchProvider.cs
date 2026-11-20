using Cantus.Core.Models;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// A single lyrics source that can participate in the fallback chain.
/// Implementations must classify their result (found / authoritative miss /
/// transiently unavailable) so <see cref="CachedLyricsService"/> can decide
/// whether a full-chain miss is safe to negative-cache.
/// </summary>
public interface ILyricsFetchProvider
{
    /// <summary>Short source name for logs (e.g. "LRCLIB", "NetEase").</summary>
    string ProviderName { get; }

    Task<LyricsFetchResult> FetchLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every lyrics match this provider has for <paramref name="track"/>,
    /// so the owner can pick one when the automatic match is wrong. Throws when
    /// the provider cannot be reached or rejects the request; callers combining
    /// providers (see <see cref="LyricsCandidateService"/>) treat that as the
    /// provider being unavailable.
    /// </summary>
    Task<IReadOnlyList<LyricsCandidate>> GetCandidatesAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default);
}
