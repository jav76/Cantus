namespace Cantus.Core.Search;

/// <summary>
/// Interns tokens and counts document frequencies for a lyric corpus. Not
/// thread-safe: one writer adds and removes documents, then publishes an
/// immutable snapshot with <see cref="Build"/>. Ids never change, so documents
/// built earlier stay valid against later snapshots.
/// </summary>
public sealed class LyricTermTableBuilder
{
    private readonly List<string> _terms = new();
    private readonly List<string> _stems = new();
    private readonly List<string?> _phoneticKeys = new();
    private readonly List<bool> _isCjk = new();
    private readonly List<int> _documentFrequencies = new();
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private int _documentCount;

    public int DocumentCount => _documentCount;

    /// <summary>Adds a document, counting each distinct term once toward its frequency.</summary>
    public LyricDocument AddDocument(NormalizedLyricText text)
    {
        int[] tokens = new int[text.Tokens.Length];
        HashSet<int> distinct = new();
        for (int i = 0; i < tokens.Length; i++)
        {
            int id = Intern(text.Tokens[i]);
            tokens[i] = id;
            if (distinct.Add(id))
            {
                _documentFrequencies[id]++;
            }
        }

        _documentCount++;
        return new LyricDocument(tokens, text.LineStarts, text.Lines);
    }

    /// <summary>Removes a document previously returned by <see cref="AddDocument"/>.</summary>
    public void RemoveDocument(LyricDocument document)
    {
        foreach (int id in document.Tokens.Distinct())
        {
            _documentFrequencies[id] = Math.Max(0, _documentFrequencies[id] - 1);
        }

        _documentCount = Math.Max(0, _documentCount - 1);
    }

    public LyricTermTable Build() =>
        new(
            _terms.ToArray(),
            _stems.ToArray(),
            _phoneticKeys.ToArray(),
            _isCjk.ToArray(),
            _documentFrequencies.ToArray(),
            new Dictionary<string, int>(_ids, StringComparer.Ordinal),
            _documentCount);

    private int Intern(string term)
    {
        if (_ids.TryGetValue(term, out int id))
        {
            return id;
        }

        id = _terms.Count;
        bool isCjk = LyricTextNormalizer.IsCjkToken(term);
        _terms.Add(term);
        _stems.Add(isCjk ? term : LightStemmer.Stem(term));
        _phoneticKeys.Add(isCjk ? null : PhoneticKey.Compute(term));
        _isCjk.Add(isCjk);
        _documentFrequencies.Add(0);
        _ids.Add(term, id);
        return id;
    }
}
