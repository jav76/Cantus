namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// One song a provider returned for a fragment search.
/// </summary>
/// <param name="Candidate">The song as the provider reports it.</param>
/// <param name="SearchableText">
/// The lyric text the provider returned for it, without markup or timestamps:
/// a matched excerpt (NetEase) or the full lyrics (LRCLIB). Empty when the
/// provider returned none. The fragment is scored against this text.
/// </param>
public sealed record LyricsFragmentHit(LyricsCandidate Candidate, string SearchableText);
