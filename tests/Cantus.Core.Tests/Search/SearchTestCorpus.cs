using Cantus.Core.Search;

namespace Cantus.Core.Tests.Search;

/// <summary>Builds a term table and documents from raw lyrics, the way the search index does.</summary>
internal sealed class SearchTestCorpus
{
    private SearchTestCorpus(LyricTermTable table, LyricDocument[] documents)
    {
        Table = table;
        Documents = documents;
    }

    public LyricTermTable Table { get; }

    public LyricDocument[] Documents { get; }

    public static SearchTestCorpus Build(IEnumerable<string> lyrics)
    {
        LyricTermTableBuilder builder = new();
        LyricDocument[] documents = lyrics
            .Select(text => builder.AddDocument(LyricTextNormalizer.Normalize(text)))
            .ToArray();
        return new SearchTestCorpus(builder.Build(), documents);
    }

    public static SearchTestCorpus Build(params string[] lyrics) => Build((IEnumerable<string>)lyrics);

    public LyricFragmentScorer CreateScorer(string fragment) =>
        new(LyricTextNormalizer.PrepareFragment(fragment), Table);

    public LyricMatch Score(string fragment, int documentIndex) =>
        CreateScorer(fragment).Score(Documents[documentIndex]);
}
