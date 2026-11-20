namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// One provider's lyrics for a track, offered to the owner as an alternative
/// to the automatic match.
/// </summary>
/// <param name="Provider">Source name, matching <see cref="ILyricsFetchProvider.ProviderName"/>.</param>
/// <param name="ProviderCandidateId">The provider's own id for these lyrics (LRCLIB record id, NetEase song id).</param>
/// <param name="Title">Track title as the provider reports it.</param>
/// <param name="Artist">Artist as the provider reports it.</param>
/// <param name="Album">Album as the provider reports it, when known.</param>
/// <param name="Duration">Track duration as the provider reports it; zero when unknown.</param>
/// <param name="IsSynced">True when the lyrics carry line timestamps.</param>
/// <param name="IsInstrumental">True when the provider marks the track as having no lyrics.</param>
/// <param name="Preview">The first non-blank lyric lines (two at most).</param>
public sealed record LyricsCandidate(
    string Provider,
    string ProviderCandidateId,
    string Title,
    string Artist,
    string? Album,
    TimeSpan Duration,
    bool IsSynced,
    bool IsInstrumental,
    IReadOnlyList<string> Preview);
