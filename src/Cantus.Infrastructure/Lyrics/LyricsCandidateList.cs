namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Candidates gathered from every enabled provider. A provider that failed
/// contributes no candidates and is named in <see cref="UnavailableProviders"/>
/// instead of failing the whole lookup.
/// </summary>
public sealed record LyricsCandidateList(
    IReadOnlyList<LyricsCandidate> Candidates,
    IReadOnlyList<string> UnavailableProviders);
