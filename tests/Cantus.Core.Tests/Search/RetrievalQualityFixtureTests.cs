using System.Diagnostics;
using System.Globalization;
using Cantus.Core.Search;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Cantus.Core.Tests.Search;

/// <summary>
/// Retrieval quality of the full scan (scorer plus ranker) over public-domain songs and
/// seeded synthetic distractors. The floors are pinned from a measured run: a change that
/// lowers one is a regression to explain, a change that raises one should raise the floor.
/// </summary>
public class RetrievalQualityFixtureTests
{
    private const int DISTRACTOR_SEED = 1234;
    private const int DISTRACTOR_COUNT = 200;
    private const int TIMING_SEED = 5678;
    private const int TIMING_SONG_COUNT = 5000;
    private const int VERSE_LINES = 12;
    private const int CHORUS_LINES = 4;
    private const int CHORUS_REPEATS = 3;
    private const int MIN_LINE_WORDS = 5;
    private const int MAX_LINE_WORDS = 8;
    private const int CLICHE_LINE_COUNT = 60;
    private const double VERSE_CLICHE_RATE = 0.12;
    private const double CHORUS_CLICHE_RATE = 0.15;
    private const double CLICHE_MUTATION_RATE = 0.30;
    private const int LOW_MARGIN_PERCENTILE = 10;
    private const int MEDIAN_PERCENTILE = 50;
    private const int PERCENT = 100;

    private const string EXACT = "exact";
    private const string WRONG_WORD = "wrongWord";
    private const string DROPPED_FILLER = "droppedFiller";
    private const string TYPO = "typo";
    private const string STITCHED = "stitched";
    private const string CONTRACTION = "contraction";
    private const string ACCENTLESS = "accentless";
    private const string CJK = "cjk";
    private const string NOT_IN_CORPUS = "notInCorpus";

    // Floors measured on the first full-scan run (seed 1234, 200 distractors): every category
    // ranked the right work first; two contraction expansions scored 0.67 and 0.66 (ambiguous).
    // Correct-top margins were p10 0.803 and p50 0.971, far above ACCEPT_MARGIN, and the best
    // song not in the corpus scored 0.368, below RESULT_MIN_SCORE, so ACCEPT_MARGIN stays 0.10.
    private const double MIN_EXACT_TOP1 = 1.0;
    private const double MIN_WRONG_WORD_TOP1 = 1.0;
    private const double MIN_DROPPED_FILLER_TOP1 = 1.0;
    private const double MIN_TYPO_TOP1 = 1.0;
    private const double MIN_STITCHED_TOP1 = 1.0;
    private const double MIN_CONTRACTION_TOP1 = 1.0;
    private const double MIN_ACCENTLESS_TOP1 = 1.0;
    private const double MIN_CJK_TOP1 = 1.0;
    private const double MIN_IN_CORPUS_ACCEPT_RATE = 0.95;
    private const double MIN_LOW_MARGIN = 0.80;
    private const double MIN_MEDIAN_MARGIN = 0.95;
    private const int MAX_NOT_IN_CORPUS_ACCEPTS = 0;
    private const double MAX_NOT_IN_CORPUS_SCORE = 0.40;

    private const string BALL_GAME = """
        Take me out to the ball game,
        Take me out with the crowd;
        Buy me some peanuts and Cracker Jack,
        I don't care if I never get back.
        Let me root, root, root for the home team,
        If they don't win, it's a shame.
        For it's one, two, three strikes, you're out,
        At the old ball game.
        """;

    private const string DAISY_BELL = """
        Daisy, Daisy, give me your answer, do!
        I'm half crazy, all for the love of you!
        It won't be a stylish marriage,
        I can't afford a carriage,
        But you'll look sweet upon the seat
        Of a bicycle built for two!
        """;

    private const string AMAZING_GRACE = """
        Amazing grace! How sweet the sound
        That saved a wretch like me!
        I once was lost, but now am found;
        Was blind, but now I see.
        'Twas grace that taught my heart to fear,
        And grace my fears relieved;
        How precious did that grace appear
        The hour I first believed!
        Through many dangers, toils and snares,
        I have already come;
        'Tis grace hath brought me safe thus far,
        And grace will lead me home.
        """;

    private const string AULD_LANG_SYNE = """
        Should auld acquaintance be forgot,
        And never brought to mind?
        Should auld acquaintance be forgot,
        And auld lang syne?
        For auld lang syne, my dear,
        For auld lang syne,
        We'll tak a cup o' kindness yet,
        For auld lang syne.
        """;

    private const string SHENANDOAH = """
        Oh Shenandoah, I long to see you,
        Away, you rolling river.
        Oh Shenandoah, I long to see you,
        Away, I'm bound away
        'Cross the wide Missouri.
        Oh Shenandoah, I love your daughter,
        Away, you rolling river.
        For her I'd cross your roaming waters,
        Away, I'm bound away
        'Cross the wide Missouri.
        """;

    private const string AU_CLAIR_DE_LA_LUNE = """
        Au clair de la lune,
        Mon ami Pierrot,
        Prête-moi ta plume
        Pour écrire un mot.
        Ma chandelle est morte,
        Je n'ai plus de feu.
        Ouvre-moi ta porte
        Pour l'amour de Dieu.
        """;

    private const string FRERE_JACQUES = """
        Frère Jacques, frère Jacques,
        Dormez-vous? Dormez-vous?
        Sonnez les matines, sonnez les matines,
        Ding, dang, dong. Ding, dang, dong.
        """;

    private const string JING_YE_SI = "床前明月光，\n疑是地上霜。\n举头望明月，\n低头思故乡。";

    private const string JIANG_JIN_JIU = """
        君不见黄河之水天上来，奔流到海不复回。
        君不见高堂明镜悲白发，朝如青丝暮成雪。
        人生得意须尽欢，莫使金樽空对月。
        天生我材必有用，千金散尽还复来。
        """;

    /// <summary>Shares the title and the opening line of Daisy Bell, but is a different song.</summary>
    private const string OTHER_DAISY_BELL = """
        Daisy, Daisy, give me your answer, do,
        the lanterns are low,
        the harbour is quiet and the night is slow,
        I wait by the water for a boat that never comes,
        and count the stars like silver drums.
        """;

    private static readonly string[] Vocabulary =
    [
        "love", "heart", "night", "day", "baby", "time", "light", "dream", "fire", "rain",
        "road", "home", "soul", "sky", "world", "eyes", "hand", "girl", "boy", "dance",
        "tonight", "forever", "never", "always", "again", "away", "down", "over", "under", "back",
        "run", "fall", "hold", "feel", "know", "want", "need", "say", "see", "take",
        "give", "make", "find", "lose", "burn", "shine", "cry", "sing", "call", "wait",
        "the", "a", "my", "your", "we", "you", "i", "me", "it", "is",
        "and", "but", "so", "in", "on", "to", "of", "with", "for", "all",
        "summer", "winter", "river", "ocean", "city", "street", "window", "door", "song", "radio",
        "golden", "broken", "lonely", "wild", "cold", "sweet", "blue", "dark", "young", "free",
        "moon", "star", "sun", "wind", "shadow", "train", "highway", "midnight", "morning", "evening",
        "whisper", "thunder", "silver", "diamond", "paper", "glass", "stone", "echo", "memory", "story",
        "tomorrow", "yesterday", "somebody", "nobody", "everything", "nothing", "alive", "falling", "flying", "dreaming",
    ];

    private static readonly FixtureSong[] Songs =
    [
        new("track-ball-game", "ball-game", "Take Me Out to the Ball Game", "Albert Von Tilzer", BALL_GAME),
        new("track-daisy", "daisy", "Daisy Bell", "Harry Dacre", DAISY_BELL),
        new("track-other-daisy", "other-daisy", "Daisy Bell", "The Harbour Lights", OTHER_DAISY_BELL),
        new("track-grace", "grace", "Amazing Grace", "John Newton", AMAZING_GRACE),
        new("track-grace-remaster", "grace", "Amazing Grace - 2011 Remaster", "John Newton", AMAZING_GRACE),
        new("track-auld", "auld", "Auld Lang Syne", "Robert Burns", AULD_LANG_SYNE),
        new("track-shenandoah", "shenandoah", "Shenandoah", "Traditional", SHENANDOAH),
        new("track-lune", "lune", "Au clair de la lune", "Traditionnel", AU_CLAIR_DE_LA_LUNE),
        new("track-jacques", "jacques", "Frère Jacques", "Traditionnel", FRERE_JACQUES),
        new("track-jing-ye-si", "jing-ye-si", "静夜思", "李白", JING_YE_SI),
        new("track-jiang-jin-jiu", "jiang-jin-jiu", "将进酒", "李白", JIANG_JIN_JIU),
    ];

    private static readonly TaggedFragment[] Fragments =
    [
        new(EXACT, "buy me some peanuts and cracker jack", "ball-game"),
        new(EXACT, "but youll look sweet upon the seat", "daisy"),
        new(EXACT, "that saved a wretch like me", "grace"),
        new(EXACT, "should auld acquaintance be forgot", "auld"),
        new(EXACT, "oh shenandoah i long to see you", "shenandoah"),
        new(EXACT, "ma chandelle est morte", "lune"),
        new(EXACT, "sonnez les matines sonnez les matines", "jacques"),
        new(EXACT, "床前明月光疑是地上霜", "jing-ye-si"),

        new(WRONG_WORD, "take me out to the baseball game", "ball-game"),
        new(WRONG_WORD, "it wont be a stylish wedding", "daisy"),
        new(WRONG_WORD, "amazing grace how sweet the song", "grace"),
        new(WRONG_WORD, "for auld lang syne my friend", "auld"),
        new(WRONG_WORD, "oh shenandoah i long to hear you", "shenandoah"),
        new(WRONG_WORD, "through many dangers toils and traps", "grace"),

        new(DROPPED_FILLER, "take me out to ball game", "ball-game"),
        new(DROPPED_FILLER, "half crazy all for love of you", "daisy"),
        new(DROPPED_FILLER, "amazing grace how sweet sound", "grace"),
        new(DROPPED_FILLER, "should auld acquaintance forgot", "auld"),
        new(DROPPED_FILLER, "i long to see you away rolling river", "shenandoah"),

        new(TYPO, "buy me some peanuts and craker jack", "ball-game"),
        new(TYPO, "it wont be a stylish mariage", "daisy"),
        new(TYPO, "amazing grase how sweet the sound", "grace"),
        new(TYPO, "should auld aquaintance be forgot", "auld"),
        new(TYPO, "oh shenandoh i long to see you away you roling river", "shenandoah"),

        new(STITCHED, "never get back let me root root root", "ball-game"),
        new(STITCHED, "look sweet upon the seat of a bicycle built for two", "daisy"),
        new(STITCHED, "the hour i first believed through many dangers", "grace"),
        new(STITCHED, "away im bound away cross the wide missouri", "shenandoah"),
        new(STITCHED, "ma chandelle est morte je nai plus de feu", "lune"),

        new(CONTRACTION, "i do not care if i never get back", "ball-game"),
        new(CONTRACTION, "i am half crazy all for the love of you", "daisy"),
        new(CONTRACTION, "it was grace that taught my heart to fear", "grace"),
        new(CONTRACTION, "we will take a cup of kindness yet", "auld"),
        new(CONTRACTION, "it will not be a stylish marriage", "daisy"),

        new(ACCENTLESS, "prete moi ta plume pour ecrire un mot", "lune"),
        new(ACCENTLESS, "frere jacques frere jacques dormez vous", "jacques"),
        new(ACCENTLESS, "pour lamour de dieu ouvre moi ta porte", "lune"),

        new(CJK, "举头望明月低头思故乡", "jing-ye-si"),
        new(CJK, "床前明月光疑似地上霜", "jing-ye-si"),
        new(CJK, "君不见黄河之水天上来奔流到海不复回君不见高堂明镜悲白发朝如青", "jiang-jin-jiu"),
        new(CJK, "人生得意须尽欢莫使金樽空对月", "jiang-jin-jiu"),

        new(NOT_IN_CORPUS, "twinkle twinkle little star how i wonder what you are", null),
        new(NOT_IN_CORPUS, "row row row your boat gently down the stream", null),
        new(NOT_IN_CORPUS, "mary had a little lamb its fleece was white as snow", null),
        new(NOT_IN_CORPUS, "oh my darling oh my darling oh my darling clementine", null),
        new(NOT_IN_CORPUS, "home home on the range where the deer and the antelope play", null),
        new(NOT_IN_CORPUS, "she will be coming round the mountain when she comes", null),
        new(NOT_IN_CORPUS, "london bridge is falling down my fair lady", null),
        new(NOT_IN_CORPUS, "take me home to the river where the wild wind blows", null),
        new(NOT_IN_CORPUS, "i feel the fire burning in my heart tonight", null),
        new(NOT_IN_CORPUS, "hold my hand and we will dance forever", null),
        new(NOT_IN_CORPUS, "the golden river runs down to the ocean", null),
        new(NOT_IN_CORPUS, "春眠不觉晓处处闻啼鸟", null),
        new(NOT_IN_CORPUS, "白日依山尽黄河入海流", null),
    ];

    private readonly ITestOutputHelper _output;

    public RetrievalQualityFixtureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FullScan_MeetsThePinnedQualityFloors()
    {
        FixtureCorpus corpus = FixtureCorpus.Build(Songs.Concat(GenerateDistractors(DISTRACTOR_SEED, DISTRACTOR_COUNT)));
        Dictionary<string, List<FragmentResult>> byCategory = Fragments
            .Select(fragment => Evaluate(corpus, fragment))
            .GroupBy(result => result.Fragment.Category)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach ((string category, List<FragmentResult> results) in byCategory)
        {
            _output.WriteLine(
                "{0,-14} n={1,2} top1={2:P0} accepted={3:P0} wrongAccepts={4}",
                category,
                results.Count,
                Rate(results, r => r.TopIsExpected),
                Rate(results, r => r.Ranking.Outcome == SongMatchOutcome.Accepted),
                results.Count(r => r.Ranking.Outcome == SongMatchOutcome.Accepted && !r.TopIsExpected));
            foreach (FragmentResult result in results.Where(r => !r.TopIsExpected || r.Ranking.Outcome != SongMatchOutcome.Accepted))
            {
                _output.WriteLine(
                    "    {0}: top={1:F3} margin={2:F3} bestOther={3:F3} outcome={4} head={5}",
                    result.Fragment.Text,
                    result.Ranking.TopScore,
                    result.Ranking.Margin,
                    result.BestOtherScore,
                    result.Ranking.Outcome,
                    result.Ranking.Guesses.Count > 0 ? result.Ranking.Guesses[0].Head.SpotifyTrackId : "-");
            }
        }

        List<FragmentResult> inCorpus = byCategory
            .Where(pair => pair.Key != NOT_IN_CORPUS)
            .SelectMany(pair => pair.Value)
            .ToList();
        double[] correctMargins = inCorpus
            .Where(r => r.TopIsExpected)
            .Select(r => r.Ranking.Margin)
            .Order()
            .ToArray();
        List<FragmentResult> notInCorpus = byCategory[NOT_IN_CORPUS];
        double lowMargin = Percentile(correctMargins, LOW_MARGIN_PERCENTILE);
        double medianMargin = Percentile(correctMargins, MEDIAN_PERCENTILE);
        double inCorpusAcceptRate = Rate(inCorpus, r => r.TopIsExpected && r.Ranking.Outcome == SongMatchOutcome.Accepted);
        int falseAccepts = notInCorpus.Count(r => r.Ranking.Outcome == SongMatchOutcome.Accepted);
        double notInCorpusMaxScore = notInCorpus.Max(r => r.BestOtherScore);

        _output.WriteLine(
            "in-corpus accept rate={0:P0}; margin p10={1:F3} p50={2:F3}; best other work p50={3:F3} max={4:F3}; "
                + "notInCorpus max raw score={5:F3}",
            inCorpusAcceptRate,
            lowMargin,
            medianMargin,
            Percentile(inCorpus.Select(r => r.BestOtherScore).Order().ToArray(), MEDIAN_PERCENTILE),
            inCorpus.Max(r => r.BestOtherScore),
            notInCorpusMaxScore);

        using (new FluentAssertions.Execution.AssertionScope())
        {
            Rate(byCategory[EXACT], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_EXACT_TOP1);
            byCategory[EXACT].Should().OnlyContain(r => r.Ranking.Outcome == SongMatchOutcome.Accepted);
            falseAccepts.Should().BeLessThanOrEqualTo(MAX_NOT_IN_CORPUS_ACCEPTS);
            Rate(byCategory[WRONG_WORD], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_WRONG_WORD_TOP1);
            Rate(byCategory[DROPPED_FILLER], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_DROPPED_FILLER_TOP1);
            Rate(byCategory[TYPO], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_TYPO_TOP1);
            Rate(byCategory[STITCHED], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_STITCHED_TOP1);
            Rate(byCategory[CONTRACTION], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_CONTRACTION_TOP1);
            Rate(byCategory[ACCENTLESS], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_ACCENTLESS_TOP1);
            Rate(byCategory[CJK], r => r.TopIsExpected).Should().BeGreaterThanOrEqualTo(MIN_CJK_TOP1);
            inCorpusAcceptRate.Should().BeGreaterThanOrEqualTo(MIN_IN_CORPUS_ACCEPT_RATE);
            lowMargin.Should().BeGreaterThanOrEqualTo(MIN_LOW_MARGIN);
            medianMargin.Should().BeGreaterThanOrEqualTo(MIN_MEDIAN_MARGIN);
            notInCorpusMaxScore.Should().BeLessThanOrEqualTo(MAX_NOT_IN_CORPUS_SCORE);
        }
    }

    [Fact]
    public void FullScan_RemasterUnderTwoTrackIdsIsOneAcceptedGuess()
    {
        FixtureCorpus corpus = FixtureCorpus.Build(Songs.Concat(GenerateDistractors(DISTRACTOR_SEED, DISTRACTOR_COUNT)));

        FragmentResult result = Evaluate(corpus, new TaggedFragment(EXACT, "i once was lost but now am found", "grace"));

        result.Ranking.Outcome.Should().Be(SongMatchOutcome.Accepted);
        result.Ranking.Guesses[0].Members.Select(m => m.SpotifyTrackId)
            .Should().BeEquivalentTo("track-grace", "track-grace-remaster");
    }

    [Fact]
    public void FullScan_SameTitleDifferentSongSharingTheLineIsAmbiguous()
    {
        FixtureCorpus corpus = FixtureCorpus.Build(Songs.Concat(GenerateDistractors(DISTRACTOR_SEED, DISTRACTOR_COUNT)));

        FragmentResult result = Evaluate(corpus, new TaggedFragment(EXACT, "daisy daisy give me your answer do", "daisy"));

        result.Ranking.Outcome.Should().Be(SongMatchOutcome.Ambiguous);
        result.Ranking.Guesses.Take(2).Select(g => g.Head.SpotifyTrackId)
            .Should().BeEquivalentTo("track-daisy", "track-other-daisy");
        result.Ranking.Guesses.Take(2).Should().OnlyContain(g => g.VersionCount == 1);
    }

    [Fact]
    public void FullScan_PrintsBuildAndScanTimeForFiveThousandSongs()
    {
        FixtureSong[] songs = GenerateDistractors(TIMING_SEED, TIMING_SONG_COUNT).ToArray();

        Stopwatch build = Stopwatch.StartNew();
        FixtureCorpus corpus = FixtureCorpus.Build(songs);
        build.Stop();

        Stopwatch scan = Stopwatch.StartNew();
        FragmentResult result = Evaluate(corpus, new TaggedFragment(NOT_IN_CORPUS, "i wait by the golden river tonight", null));
        scan.Stop();

        _output.WriteLine(
            "{0} songs: build {1} ms, scan {2} ms",
            TIMING_SONG_COUNT,
            build.ElapsedMilliseconds,
            scan.ElapsedMilliseconds);
        result.Ranking.Should().NotBeNull();
    }

    private static FragmentResult Evaluate(FixtureCorpus corpus, TaggedFragment fragment)
    {
        LyricFragmentScorer scorer = new(LyricTextNormalizer.PrepareFragment(fragment.Text), corpus.Table);
        List<SongCandidate> candidates = new(corpus.Entries.Length);
        foreach (FixtureEntry entry in corpus.Entries)
        {
            LyricMatch match = scorer.Score(entry.Document);
            candidates.Add(new SongCandidate(
                SongCandidateOrigin.Local,
                "Local",
                entry.Song.TrackId,
                entry.Song.Title,
                entry.Song.Artist,
                null,
                null,
                match.Score,
                true,
                entry.Document.GetMatchedLines(match),
                entry.Tokens));
        }

        SongGuessRanking ranking = SongGuessRanker.Rank(candidates);
        double bestOtherScore = candidates
            .Where(c => fragment.ExpectedWork is null || corpus.WorkOf(c.SpotifyTrackId) != fragment.ExpectedWork)
            .Max(c => c.Score);
        bool topIsExpected = fragment.ExpectedWork is not null
            && ranking.Guesses.Count > 0
            && ranking.Guesses[0].Members.Any(m => corpus.WorkOf(m.SpotifyTrackId) == fragment.ExpectedWork);
        return new FragmentResult(fragment, ranking, topIsExpected, bestOtherScore);
    }

    /// <summary>
    /// Synthetic songs with a repeated chorus. Some lines come from a shared pool of
    /// cliché lines, a few with one word changed, so distractors also resemble each other.
    /// </summary>
    private static IEnumerable<FixtureSong> GenerateDistractors(int seed, int count)
    {
        Random random = new(seed);
        string[] cliches = Enumerable.Range(0, CLICHE_LINE_COUNT).Select(_ => RandomLine(random)).ToArray();
        for (int i = 0; i < count; i++)
        {
            string[] chorus = Enumerable
                .Range(0, CHORUS_LINES)
                .Select(_ => DistractorLine(random, cliches, CHORUS_CLICHE_RATE))
                .ToArray();
            List<string> lines = new();
            for (int verse = 0; verse < CHORUS_REPEATS; verse++)
            {
                lines.AddRange(Enumerable
                    .Range(0, VERSE_LINES / CHORUS_REPEATS)
                    .Select(_ => DistractorLine(random, cliches, VERSE_CLICHE_RATE)));
                lines.AddRange(chorus);
            }

            string id = i.ToString(CultureInfo.InvariantCulture);
            yield return new FixtureSong(
                $"track-synthetic-{id}",
                $"synthetic-{id}",
                $"{RandomWord(random)} {RandomWord(random)}",
                $"Synthetic Artist {id}",
                string.Join('\n', lines));
        }
    }

    private static string DistractorLine(Random random, string[] cliches, double clicheRate)
    {
        if (random.NextDouble() >= clicheRate)
        {
            return RandomLine(random);
        }

        string[] words = cliches[random.Next(cliches.Length)].Split(' ');
        if (random.NextDouble() < CLICHE_MUTATION_RATE)
        {
            words[random.Next(words.Length)] = RandomWord(random);
        }

        return string.Join(' ', words);
    }

    private static string RandomLine(Random random)
    {
        int length = random.Next(MIN_LINE_WORDS, MAX_LINE_WORDS + 1);
        return string.Join(' ', Enumerable.Range(0, length).Select(_ => RandomWord(random)));
    }

    private static string RandomWord(Random random) => Vocabulary[random.Next(Vocabulary.Length)];

    private static double Rate(IReadOnlyCollection<FragmentResult> results, Func<FragmentResult, bool> predicate) =>
        results.Count == 0 ? 0 : (double)results.Count(predicate) / results.Count;

    private static double Percentile(double[] sorted, int percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        int index = (int)Math.Floor((double)percentile / PERCENT * (sorted.Length - 1));
        return sorted[index];
    }

    private sealed record FixtureSong(string TrackId, string WorkId, string Title, string Artist, string Lyrics);

    private sealed record TaggedFragment(string Category, string Text, string? ExpectedWork);

    /// <param name="BestOtherScore">The best raw score of any other work, before the ranker drops low scores.</param>
    private sealed record FragmentResult(
        TaggedFragment Fragment,
        SongGuessRanking Ranking,
        bool TopIsExpected,
        double BestOtherScore);

    private sealed record FixtureEntry(FixtureSong Song, LyricDocument Document, string[] Tokens);

    private sealed class FixtureCorpus
    {
        private readonly Dictionary<string, string> _workByTrack;

        private FixtureCorpus(LyricTermTable table, FixtureEntry[] entries)
        {
            Table = table;
            Entries = entries;
            _workByTrack = entries.ToDictionary(e => e.Song.TrackId, e => e.Song.WorkId, StringComparer.Ordinal);
        }

        public LyricTermTable Table { get; }

        public FixtureEntry[] Entries { get; }

        public static FixtureCorpus Build(IEnumerable<FixtureSong> songs)
        {
            LyricTermTableBuilder builder = new();
            FixtureEntry[] entries = songs
                .Select(song =>
                {
                    NormalizedLyricText text = LyricTextNormalizer.Normalize(song.Lyrics);
                    return new FixtureEntry(song, builder.AddDocument(text), text.Tokens);
                })
                .ToArray();
            return new FixtureCorpus(builder.Build(), entries);
        }

        public string? WorkOf(string? trackId) =>
            trackId is not null && _workByTrack.TryGetValue(trackId, out string? work) ? work : null;
    }
}
