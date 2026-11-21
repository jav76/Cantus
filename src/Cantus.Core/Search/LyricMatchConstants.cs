namespace Cantus.Core.Search;

/// <summary>
/// Weights and thresholds of the lyric fragment matcher. The scoring values are
/// copied from the calibrated prototype: <c>Final = 0.60 * Align + 0.25 * TrigramDice + 0.15 * IdfBoW</c>.
/// Every score is a match score in 0..1, not a probability.
/// </summary>
public static class LyricMatchConstants
{
    public const double ALIGN_WEIGHT = 0.60;
    public const double DICE_WEIGHT = 0.25;
    public const double IDF_BOW_WEIGHT = 0.15;

    /// <summary>Alignment credit for identical tokens.</summary>
    public const double MATCH_EXACT = 1.0;

    /// <summary>Alignment credit for tokens with the same light stem (stem keeps at least <see cref="MIN_STEM_LENGTH"/> characters).</summary>
    public const double MATCH_STEM = 0.8;

    /// <summary>Alignment credit for tokens within the OSA edit allowance.</summary>
    public const double MATCH_FUZZY = 0.75;

    /// <summary>Alignment credit for tokens with the same phonetic key.</summary>
    public const double MATCH_PHONETIC = 0.6;

    /// <summary>Alignment cost of a substituted word.</summary>
    public const double MISMATCH_PENALTY = 0.35;

    /// <summary>Alignment cost of an extra word in the fragment.</summary>
    public const double GAP_FRAGMENT_PENALTY = 0.35;

    /// <summary>Alignment cost of a skipped lyric word.</summary>
    public const double GAP_LYRIC_PENALTY = 0.12;

    public const int MIN_STEM_LENGTH = 3;

    /// <summary>Only words this long are stripped of the "in" suffix ("nothin" but not "lovin").</summary>
    public const int MIN_LENGTH_FOR_IN_SUFFIX = 6;

    /// <summary>Words this long drop a final "e" when no other suffix applies.</summary>
    public const int MIN_LENGTH_FOR_FINAL_E_DROP = 5;

    /// <summary>One OSA edit is allowed when the shorter word has at least this many characters.</summary>
    public const int FUZZY_ONE_EDIT_MIN_LENGTH = 4;

    /// <summary>Two OSA edits are allowed when the shorter word has at least this many characters.</summary>
    public const int FUZZY_TWO_EDITS_MIN_LENGTH = 8;

    public const int MIN_PHONETIC_TOKEN_LENGTH = 3;
    public const int MIN_PHONETIC_KEY_LENGTH = 2;

    /// <summary>Below this corpus size every fragment word weighs the same in IdfBoW.</summary>
    public const int IDF_MIN_DOCUMENTS = 50;

    /// <summary>BM25 idf smoothing: <c>ln(1 + (N - df + 0.5) / (df + 0.5))</c>.</summary>
    public const double BM25_IDF_SMOOTHING = 0.5;

    /// <summary>A lookup is accepted when the top guess scores at least this much...</summary>
    public const double ACCEPT_SCORE = 0.70;

    /// <summary>...and leads the next different song by at least this margin.</summary>
    public const double ACCEPT_MARGIN = 0.10;

    /// <summary>Below acceptance but at least this score, the outcome is ambiguous.</summary>
    public const double AMBIGUOUS_SCORE = 0.50;

    /// <summary>Scored guesses below this are not returned.</summary>
    public const double RESULT_MIN_SCORE = 0.40;

    public const int MAX_GUESSES = 5;

    /// <summary>Provider hits the scorer could not confirm, appended after the scored guesses.</summary>
    public const int MAX_UNVERIFIED_GUESSES = 3;

    /// <summary>Absorbs floating-point error when comparing a score difference with a threshold.</summary>
    public const double SCORE_EPSILON = 1e-9;

    /// <summary>Two local lyrics with equal title cores are one work at this distinct-token Jaccard.</summary>
    public const double SAME_LYRICS_JACCARD = 0.80;

    /// <summary>An external excerpt with an equal title core is the same work when it aligns at this score.</summary>
    public const double SAME_EXCERPT_ALIGN = 0.90;

    public const int MIN_WORD_TOKENS = 3;
    public const int MAX_WORD_TOKENS = 24;
    public const int MIN_CJK_TOKENS = 4;
    public const int MAX_CJK_TOKENS = 48;

    /// <summary>Fragments longer than this are rejected before normalization.</summary>
    public const int MAX_FRAGMENT_CHARACTERS = 200;

    /// <summary>A matched window is reported as at most this many original lines.</summary>
    public const int MAX_MATCHED_LINES = 2;
}
