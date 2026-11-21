namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// A lyrics provider that can search by a remembered lyric fragment. Discovered
/// on the <see cref="ILyricsFetchProvider"/> chain with <c>OfType</c>, so a
/// provider disabled in configuration is never searched. Implementations never
/// write the lyrics cache and tag every request as lookup traffic for
/// <see cref="ProviderRateGate"/>.
/// </summary>
public interface ILyricsFragmentSearchProvider
{
    /// <summary>Short source name, matching <see cref="ILyricsFetchProvider.ProviderName"/>.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Songs the provider returns for <paramref name="query"/>, in the
    /// provider's own order. An empty list is a definitive miss; throwing means
    /// the provider is unavailable (unreachable, rate limited or rejecting the
    /// request, see <see cref="ProviderRateLimitedException"/> and
    /// <see cref="ProviderPayloadRejectedException"/>).
    /// </summary>
    Task<IReadOnlyList<LyricsFragmentHit>> SearchByFragmentAsync(
        LyricsFragmentQuery query,
        CancellationToken cancellationToken = default);
}
