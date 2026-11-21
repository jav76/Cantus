namespace Cantus.Core.Search;

/// <summary>
/// Groups versions of the same work, then decides on the group heads.
/// <list type="bullet">
/// <item>Verified candidates at or above <see cref="LyricMatchConstants.RESULT_MIN_SCORE"/> are
/// grouped greedily in score order against each group's head. Two candidates are one work
/// when their title cores are equal and either their primary artist keys are equal or their
/// lyric evidence agrees: distinct-token Jaccard of at least
/// <see cref="LyricMatchConstants.SAME_LYRICS_JACCARD"/> for two local lyrics, or an external
/// excerpt aligning to the other at <see cref="LyricMatchConstants.SAME_EXCERPT_ALIGN"/> or more.
/// Two songs that merely share a title stay apart, so a line both contain stays ambiguous.</item>
/// <item>Accepted: top at least <see cref="LyricMatchConstants.ACCEPT_SCORE"/> and at least
/// <see cref="LyricMatchConstants.ACCEPT_MARGIN"/> ahead of the second head. Ambiguous: top at
/// least <see cref="LyricMatchConstants.AMBIGUOUS_SCORE"/>. Otherwise none.</item>
/// <item>Unverified candidates never join groups, the margin or the outcome; up to
/// <see cref="LyricMatchConstants.MAX_UNVERIFIED_GUESSES"/> are appended after the scored guesses.</item>
/// </list>
/// Local wins ties because it has a track id.
/// </summary>
public static class SongGuessRanker
{
    public static SongGuessRanking Rank(IEnumerable<SongCandidate> candidates)
    {
        List<SongCandidate> all = candidates.ToList();

        List<RankedCandidate> verified = all
            .Where(c => c.Verified && c.Score >= LyricMatchConstants.RESULT_MIN_SCORE)
            .Select(c => new RankedCandidate(c))
            .OrderBy(c => c.Candidate, CandidateOrder.Instance)
            .ToList();

        List<(RankedCandidate Head, List<SongCandidate> Members)> groups = new();
        foreach (RankedCandidate candidate in verified)
        {
            int groupIndex = groups.FindIndex(g => IsSameWork(g.Head, candidate));
            if (groupIndex >= 0)
            {
                groups[groupIndex].Members.Add(candidate.Candidate);
            }
            else
            {
                groups.Add((candidate, new List<SongCandidate> { candidate.Candidate }));
            }
        }

        double top = groups.Count > 0 ? groups[0].Head.Candidate.Score : 0;
        double second = groups.Count > 1 ? groups[1].Head.Candidate.Score : 0;
        double margin = top - second;

        SongMatchOutcome outcome = Decide(top, margin);

        List<SongGuessGroup> guesses = groups
            .Take(LyricMatchConstants.MAX_GUESSES)
            .Select(g => new SongGuessGroup(g.Head.Candidate, g.Members))
            .ToList();

        guesses.AddRange(all
            .Where(c => !c.Verified)
            .OrderBy(c => c, CandidateOrder.Instance)
            .Take(LyricMatchConstants.MAX_UNVERIFIED_GUESSES)
            .Select(c => new SongGuessGroup(c, [c])));

        return new SongGuessRanking(outcome, top, margin, guesses);
    }

    /// <summary>The decision bands applied to the top group head's score and its margin.</summary>
    public static SongMatchOutcome Decide(double topScore, double margin)
    {
        if (topScore >= LyricMatchConstants.ACCEPT_SCORE
            && margin >= LyricMatchConstants.ACCEPT_MARGIN - LyricMatchConstants.SCORE_EPSILON)
        {
            return SongMatchOutcome.Accepted;
        }

        return topScore >= LyricMatchConstants.AMBIGUOUS_SCORE ? SongMatchOutcome.Ambiguous : SongMatchOutcome.None;
    }

    private static bool IsSameWork(RankedCandidate head, RankedCandidate candidate)
    {
        if (head.TitleCore.Length == 0 || !string.Equals(head.TitleCore, candidate.TitleCore, StringComparison.Ordinal))
        {
            return false;
        }

        if (head.ArtistKey.Length > 0 && string.Equals(head.ArtistKey, candidate.ArtistKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (head.Candidate.Origin == SongCandidateOrigin.Local && candidate.Candidate.Origin == SongCandidateOrigin.Local)
        {
            return Jaccard(head.DistinctTokens, candidate.DistinctTokens) >= LyricMatchConstants.SAME_LYRICS_JACCARD;
        }

        double alignment = 0;
        if (candidate.Candidate.Origin == SongCandidateOrigin.External)
        {
            alignment = SmithWatermanAligner.AlignTokens(candidate.Candidate.LyricTokens, head.Candidate.LyricTokens);
        }

        if (head.Candidate.Origin == SongCandidateOrigin.External)
        {
            alignment = Math.Max(
                alignment,
                SmithWatermanAligner.AlignTokens(head.Candidate.LyricTokens, candidate.Candidate.LyricTokens));
        }

        return alignment >= LyricMatchConstants.SAME_EXCERPT_ALIGN;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        int intersection = a.Count(b.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    private sealed class RankedCandidate
    {
        private HashSet<string>? _distinctTokens;

        public RankedCandidate(SongCandidate candidate)
        {
            Candidate = candidate;
            TitleCore = LyricTextNormalizer.TitleCore(candidate.Title);
            ArtistKey = LyricTextNormalizer.ArtistKey(candidate.Artist);
        }

        public SongCandidate Candidate { get; }

        public string TitleCore { get; }

        public string ArtistKey { get; }

        public HashSet<string> DistinctTokens => _distinctTokens ??= new HashSet<string>(
            Candidate.LyricTokens,
            StringComparer.Ordinal);
    }

    /// <summary>Score descending, then local before external, then a stable textual order.</summary>
    private sealed class CandidateOrder : IComparer<SongCandidate>
    {
        public static CandidateOrder Instance { get; } = new();

        public int Compare(SongCandidate? x, SongCandidate? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return 1;
            }

            if (y is null)
            {
                return -1;
            }

            int byScore = y.Score.CompareTo(x.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            int byOrigin = x.Origin.CompareTo(y.Origin);
            if (byOrigin != 0)
            {
                return byOrigin;
            }

            int byTrack = string.CompareOrdinal(x.SpotifyTrackId, y.SpotifyTrackId);
            return byTrack != 0 ? byTrack : string.CompareOrdinal(x.Title, y.Title);
        }
    }
}
