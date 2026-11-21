namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// A fragment search sent to an <see cref="ILyricsFragmentSearchProvider"/>.
/// </summary>
/// <param name="Text">The fragment as the owner typed it.</param>
/// <param name="TitleGuesses">
/// Fragment windows that might be the title, best first, for providers that
/// search by title only. Providers that search lyric text ignore them.
/// </param>
public sealed record LyricsFragmentQuery(string Text, IReadOnlyList<string> TitleGuesses);
