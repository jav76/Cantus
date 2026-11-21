namespace Cantus.Core.Search;

/// <summary>
/// An immutable vocabulary with document frequencies, shared by every scorer of
/// a corpus snapshot so local lyrics and external text use one idf table.
/// Term ids are dense, start at 0 and stay stable across snapshots built by the
/// same <see cref="LyricTermTableBuilder"/>.
/// </summary>
public sealed class LyricTermTable
{
    private readonly string[] _terms;
    private readonly string[] _stems;
    private readonly string?[] _phoneticKeys;
    private readonly bool[] _isCjk;
    private readonly int[] _documentFrequencies;
    private readonly Dictionary<string, int> _ids;

    internal LyricTermTable(
        string[] terms,
        string[] stems,
        string?[] phoneticKeys,
        bool[] isCjk,
        int[] documentFrequencies,
        Dictionary<string, int> ids,
        int documentCount)
    {
        _terms = terms;
        _stems = stems;
        _phoneticKeys = phoneticKeys;
        _isCjk = isCjk;
        _documentFrequencies = documentFrequencies;
        _ids = ids;
        DocumentCount = documentCount;
    }

    public static LyricTermTable Empty { get; } = new LyricTermTableBuilder().Build();

    public int DocumentCount { get; }

    public int TermCount => _terms.Length;

    public string GetTerm(int termId) => _terms[termId];

    public int GetDocumentFrequency(int termId) => _documentFrequencies[termId];

    public bool TryGetTermId(string term, out int termId) => _ids.TryGetValue(term, out termId);

    /// <summary>The term strings of a document built against this table.</summary>
    public string[] GetTerms(LyricDocument document)
    {
        string[] terms = new string[document.Tokens.Length];
        for (int i = 0; i < terms.Length; i++)
        {
            terms[i] = _terms[document.Tokens[i]];
        }

        return terms;
    }

    internal string GetStem(int termId) => _stems[termId];

    internal string? GetPhoneticKey(int termId) => _phoneticKeys[termId];

    internal bool IsCjk(int termId) => _isCjk[termId];
}
