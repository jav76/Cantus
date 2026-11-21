namespace Cantus.Core.Search;

/// <summary>A scored external text with its matched lines and normalized tokens.</summary>
public sealed record LyricTextMatch(LyricMatch Match, IReadOnlyList<string> MatchedLines, string[] Tokens);

/// <summary>
/// Scores one prepared fragment against many lyric documents:
/// <c>Final = ALIGN_WEIGHT * Align + DICE_WEIGHT * TrigramDice + IDF_BOW_WEIGHT * IdfBoW</c>.
/// <list type="bullet">
/// <item>Align: word-level Smith-Waterman normalized by fragment length (not idf weighted).</item>
/// <item>TrigramDice: best multiset Dice of word-padded character trigrams over lyric windows
/// of n and n+1 tokens. Adjacent CJK characters inside the window contribute a bigram instead.</item>
/// <item>IdfBoW: share of the fragment's BM25 idf mass found exactly in the lyric. An
/// unknown fragment word takes the idf of its most frequent fuzzy match; below
/// <see cref="LyricMatchConstants.IDF_MIN_DOCUMENTS"/> documents all words weigh the same.</item>
/// </list>
/// Document frequencies come from the caller's <see cref="LyricTermTable"/>, so local
/// lyrics and external excerpts share one idf table. An instance caches per-term work
/// for its fragment and is not thread-safe; create one per lookup.
/// </summary>
public sealed class LyricFragmentScorer
{
    /// <summary>Exact-hit positions are tracked in a 64-bit mask.</summary>
    private const int MAX_FRAGMENT_POSITIONS = 64;

    private const int NO_GRAM = -1;
    private const int LATIN_POSITION = -2;
    private const int NO_CJK_PAIR = -3;
    private const string TRIGRAM_PREFIX = "  ";
    private const string TRIGRAM_SUFFIX = " ";
    private const int TRIGRAM_LENGTH = 3;

    /// <summary>Two 32-bit term ids are packed into one 64-bit CJK pair key.</summary>
    private const int PAIR_KEY_SHIFT = 32;

    private readonly LyricTermTable _terms;
    private readonly int _length;
    private readonly TokenFeatures[] _fragment;
    private readonly int[] _fragmentIds;
    private readonly double[] _weights;
    private readonly double _weightSum;
    private readonly double[] _mismatchRow;
    private readonly Dictionary<string, int> _gramIndex = new(StringComparer.Ordinal);
    private readonly List<int> _fragmentGramCounts = new();
    private readonly int _fragmentGramTotal;
    private readonly Dictionary<long, int> _cjkPairGrams = new();
    private readonly List<TokenFeatures> _overflowTerms = new();
    private readonly Dictionary<string, int> _overflowIds = new(StringComparer.Ordinal);
    private readonly double[] _alignScores;
    private readonly int[] _alignStarts;
    private readonly int[] _windowCounts;
    private TermInfo?[] _cache;
    private int[] _positionGrams = [];

    public LyricFragmentScorer(LyricFragment fragment, LyricTermTable terms)
    {
        if (!fragment.IsValid)
        {
            throw new ArgumentException("Only a valid fragment can be scored.", nameof(fragment));
        }

        if (fragment.Tokens.Count > MAX_FRAGMENT_POSITIONS)
        {
            throw new ArgumentException("The fragment has too many tokens.", nameof(fragment));
        }

        _terms = terms;
        _cache = new TermInfo?[terms.TermCount];
        _length = fragment.Tokens.Count;
        _fragment = fragment.Tokens.Select(TokenFeatures.From).ToArray();
        _fragmentIds = fragment.Tokens.Select(ResolveId).ToArray();
        _mismatchRow = SmithWatermanAligner.CreateMismatchRow(_length);
        _alignScores = new double[_length];
        _alignStarts = new int[_length];

        _weights = ComputeWeights(fragment.Tokens);
        _weightSum = _weights.Sum();
        _fragmentGramTotal = RegisterFragmentGrams();
        _windowCounts = new int[_fragmentGramCounts.Count];
    }

    public int FragmentLength => _length;

    /// <summary>Scores a document built against the scorer's term table (or by <see cref="CreateDocument"/>).</summary>
    public LyricMatch Score(LyricDocument document)
    {
        int[] tokens = document.Tokens;
        if (tokens.Length == 0)
        {
            return LyricMatch.None;
        }

        ulong exactHits = 0;
        foreach (int token in tokens)
        {
            exactHits |= GetTermInfo(token).ExactMask;
        }

        AlignmentResult alignment = SmithWatermanAligner.Align(
            _length,
            tokens.Length,
            column => GetTermInfo(tokens[column]).Row,
            _mismatchRow,
            _alignScores,
            _alignStarts);

        double align = Math.Min(1.0, alignment.Best / _length);
        double dice = ComputeDice(document);
        double bagOfWords = ComputeBagOfWords(exactHits);
        double score = LyricMatchConstants.ALIGN_WEIGHT * align
            + LyricMatchConstants.DICE_WEIGHT * dice
            + LyricMatchConstants.IDF_BOW_WEIGHT * bagOfWords;

        return alignment.Best > 0
            ? new LyricMatch(score, align, dice, bagOfWords, alignment.Start, alignment.End)
            : new LyricMatch(score, align, dice, bagOfWords, -1, -1);
    }

    /// <summary>
    /// Builds a document from arbitrary text (an external excerpt or lyric). Terms
    /// missing from the table get scorer-local ids with a document frequency of 0.
    /// </summary>
    public LyricDocument CreateDocument(NormalizedLyricText text)
    {
        int[] tokens = new int[text.Tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            tokens[i] = ResolveId(text.Tokens[i]);
        }

        return new LyricDocument(tokens, text.LineStarts, text.Lines);
    }

    /// <summary>Normalizes and scores arbitrary text, such as a provider's lyric excerpt.</summary>
    public LyricTextMatch ScoreText(string? text)
    {
        NormalizedLyricText normalized = LyricTextNormalizer.Normalize(text);
        LyricDocument document = CreateDocument(normalized);
        LyricMatch match = Score(document);
        return new LyricTextMatch(match, document.GetMatchedLines(match), normalized.Tokens);
    }

    private double[] ComputeWeights(IReadOnlyList<string> tokens)
    {
        double[] weights = new double[tokens.Count];
        if (_terms.DocumentCount < LyricMatchConstants.IDF_MIN_DOCUMENTS)
        {
            Array.Fill(weights, 1.0);
            return weights;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            int documentFrequency = _terms.TryGetTermId(tokens[i], out int id) ? _terms.GetDocumentFrequency(id) : 0;
            if (documentFrequency == 0)
            {
                documentFrequency = MostFrequentFuzzyMatch(tokens[i]);
            }

            weights[i] = InverseDocumentFrequency(documentFrequency);
        }

        return weights;
    }

    private double InverseDocumentFrequency(int documentFrequency)
    {
        double smoothing = LyricMatchConstants.BM25_IDF_SMOOTHING;
        return Math.Log(1 + (_terms.DocumentCount - documentFrequency + smoothing) / (documentFrequency + smoothing));
    }

    private int MostFrequentFuzzyMatch(string token)
    {
        if (LyricTextNormalizer.IsCjkToken(token))
        {
            return 0;
        }

        int best = 0;
        for (int id = 0; id < _terms.TermCount; id++)
        {
            int documentFrequency = _terms.GetDocumentFrequency(id);
            if (documentFrequency > best
                && !_terms.IsCjk(id)
                && OsaDistance.IsFuzzyMatch(token, _terms.GetTerm(id)))
            {
                best = documentFrequency;
            }
        }

        return best;
    }

    private double ComputeBagOfWords(ulong exactHits)
    {
        if (_weightSum <= 0)
        {
            return 0;
        }

        double found = 0;
        for (int i = 0; i < _length; i++)
        {
            if ((exactHits & (1UL << i)) != 0)
            {
                found += _weights[i];
            }
        }

        return found / _weightSum;
    }

    private int RegisterFragmentGrams()
    {
        int total = 0;
        for (int i = 0; i < _length; i++)
        {
            if (_fragment[i].IsCjk)
            {
                if (i > 0 && _fragment[i - 1].IsCjk)
                {
                    AddFragmentGram(_fragment[i - 1].Term + _fragment[i].Term);
                    total++;
                }
            }
            else
            {
                foreach (string gram in Trigrams(_fragment[i].Term))
                {
                    AddFragmentGram(gram);
                    total++;
                }
            }
        }

        return total;
    }

    private void AddFragmentGram(string gram)
    {
        if (!_gramIndex.TryGetValue(gram, out int index))
        {
            index = _fragmentGramCounts.Count;
            _gramIndex.Add(gram, index);
            _fragmentGramCounts.Add(0);
        }

        _fragmentGramCounts[index]++;
    }

    private double ComputeDice(LyricDocument document)
    {
        int[] tokens = document.Tokens;
        int count = tokens.Length;
        if (_positionGrams.Length < count)
        {
            _positionGrams = new int[Math.Max(count, _positionGrams.Length * 2)];
        }

        for (int j = 0; j < count; j++)
        {
            if (!GetTermInfo(tokens[j]).IsCjk)
            {
                _positionGrams[j] = LATIN_POSITION;
            }
            else
            {
                _positionGrams[j] = j > 0 && GetTermInfo(tokens[j - 1]).IsCjk
                    ? GetCjkPairGram(tokens[j - 1], tokens[j])
                    : NO_CJK_PAIR;
            }
        }

        int shortWindow = Math.Min(_length, count);
        int longWindow = Math.Min(_length + 1, count);
        double best = SlideWindow(tokens, shortWindow);
        if (longWindow != shortWindow)
        {
            best = Math.Max(best, SlideWindow(tokens, longWindow));
        }

        return best;
    }

    /// <summary>
    /// A window holds the trigrams of its Latin tokens and the bigram of each CJK
    /// position whose predecessor is also inside the window.
    /// </summary>
    private double SlideWindow(int[] tokens, int windowLength)
    {
        Array.Clear(_windowCounts);
        int overlap = 0;
        int size = 0;
        int start = 0;
        double best = 0;

        for (int j = 0; j < tokens.Length; j++)
        {
            UpdateToken(tokens, j, 1, ref overlap, ref size);
            if (j > start)
            {
                UpdatePair(j, 1, ref overlap, ref size);
            }

            if (j - start + 1 > windowLength)
            {
                UpdateToken(tokens, start, -1, ref overlap, ref size);
                UpdatePair(start + 1, -1, ref overlap, ref size);
                start++;
            }

            if (j - start + 1 == windowLength)
            {
                double dice = 2.0 * overlap / (_fragmentGramTotal + size);
                if (dice > best)
                {
                    best = dice;
                }
            }
        }

        return best;
    }

    private void UpdateToken(int[] tokens, int position, int direction, ref int overlap, ref int size)
    {
        if (_positionGrams[position] != LATIN_POSITION)
        {
            return;
        }

        TermInfo info = GetTermInfo(tokens[position]);
        size += direction * info.GramCount;
        foreach (int index in info.GramIndices)
        {
            UpdateGram(index, direction, ref overlap);
        }
    }

    private void UpdatePair(int position, int direction, ref int overlap, ref int size)
    {
        int gram = _positionGrams[position];
        if (gram is LATIN_POSITION or NO_CJK_PAIR)
        {
            return;
        }

        size += direction;
        UpdateGram(gram, direction, ref overlap);
    }

    private void UpdateGram(int index, int direction, ref int overlap)
    {
        if (index < 0)
        {
            return;
        }

        if (direction > 0)
        {
            if (_windowCounts[index] < _fragmentGramCounts[index])
            {
                overlap++;
            }

            _windowCounts[index]++;
        }
        else
        {
            _windowCounts[index]--;
            if (_windowCounts[index] < _fragmentGramCounts[index])
            {
                overlap--;
            }
        }
    }

    private int GetCjkPairGram(int previousId, int id)
    {
        long key = ((long)previousId << PAIR_KEY_SHIFT) | (uint)id;
        if (!_cjkPairGrams.TryGetValue(key, out int index))
        {
            string gram = GetFeatures(previousId).Term + GetFeatures(id).Term;
            index = _gramIndex.TryGetValue(gram, out int found) ? found : NO_GRAM;
            _cjkPairGrams.Add(key, index);
        }

        return index;
    }

    private TermInfo GetTermInfo(int id)
    {
        if (id >= _cache.Length)
        {
            Array.Resize(ref _cache, Math.Max(id + 1, _cache.Length * 2));
        }

        return _cache[id] ??= CreateTermInfo(id);
    }

    private TermInfo CreateTermInfo(int id)
    {
        TokenFeatures lyric = GetFeatures(id);
        double[]? row = null;
        ulong exactMask = 0;

        for (int i = 0; i < _length; i++)
        {
            double credit;
            if (_fragmentIds[i] == id)
            {
                credit = LyricMatchConstants.MATCH_EXACT;
                exactMask |= 1UL << i;
            }
            else
            {
                credit = LyricTokenMatcher.Score(_fragment[i], lyric);
            }

            if (credit > 0)
            {
                row ??= (double[])_mismatchRow.Clone();
                row[i] = credit;
            }
        }

        if (lyric.IsCjk)
        {
            return new TermInfo(row, exactMask, [], 1, true);
        }

        List<int> gramIndices = new();
        int gramCount = 0;
        foreach (string gram in Trigrams(lyric.Term))
        {
            gramCount++;
            if (_gramIndex.TryGetValue(gram, out int index))
            {
                gramIndices.Add(index);
            }
        }

        return new TermInfo(row, exactMask, gramIndices.ToArray(), gramCount, false);
    }

    private TokenFeatures GetFeatures(int id) =>
        id < _terms.TermCount
            ? new TokenFeatures(_terms.GetTerm(id), _terms.GetStem(id), _terms.GetPhoneticKey(id), _terms.IsCjk(id))
            : _overflowTerms[id - _terms.TermCount];

    private int ResolveId(string term)
    {
        if (_terms.TryGetTermId(term, out int id))
        {
            return id;
        }

        if (!_overflowIds.TryGetValue(term, out id))
        {
            id = _terms.TermCount + _overflowTerms.Count;
            _overflowTerms.Add(TokenFeatures.From(term));
            _overflowIds.Add(term, id);
        }

        return id;
    }

    private static IEnumerable<string> Trigrams(string term)
    {
        string padded = TRIGRAM_PREFIX + term + TRIGRAM_SUFFIX;
        for (int i = 0; i + TRIGRAM_LENGTH <= padded.Length; i++)
        {
            yield return padded.Substring(i, TRIGRAM_LENGTH);
        }
    }

    private sealed record TermInfo(double[]? Row, ulong ExactMask, int[] GramIndices, int GramCount, bool IsCjk);
}
