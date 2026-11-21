using Cantus.Core.Search;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Search;

public class LyricFragmentScorerTests
{
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

    private const string UNRELATED = """
        Twinkle, twinkle, little star,
        How I wonder what you are!
        Up above the world so high,
        Like a diamond in the sky.
        """;

    private const string JING_YE_SI = "床前明月光，\n疑是地上霜。\n举头望明月，\n低头思故乡。";

    [Fact]
    public void Score_ExactFragment_ScoresAboutOne()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME, UNRELATED);

        LyricMatch match = corpus.Score("buy me some peanuts and cracker jack", 0);

        match.Score.Should().BeApproximately(1.0, 0.001);
        match.Align.Should().BeApproximately(1.0, 0.001);
        corpus.Documents[0].GetMatchedLines(match).Should().Equal("Buy me some peanuts and Cracker Jack,");
    }

    [Theory]
    [InlineData("buy me some popcorn and cracker jack")]
    [InlineData("take me out to the baseball game")]
    [InlineData("let me shout root root for the home team")]
    public void Score_OneWrongWordInSixOrMoreTokens_StaysAtLeastAcceptScore(string fragment)
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME, UNRELATED);

        LyricMatch match = corpus.Score(fragment, 0);

        match.Score.Should().BeGreaterThanOrEqualTo(LyricMatchConstants.ACCEPT_SCORE);
    }

    [Fact]
    public void Score_FragmentStitchedAcrossTwoLines_AlignsAndReportsBothLines()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME, UNRELATED);

        LyricMatch match = corpus.Score("never get back let me root root root", 0);

        match.Align.Should().BeApproximately(1.0, 0.001);
        match.Score.Should().BeGreaterThan(0.9);
        corpus.Documents[0].GetMatchedLines(match).Should().Equal(
            "I don't care if I never get back.",
            "Let me root, root, root for the home team,");
    }

    [Fact]
    public void Score_WindowOverThreeLines_ReportsAtMostTwoLines()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME);

        LyricMatch match = corpus.Score("with the crowd buy me some peanuts and cracker jack i dont care", 0);

        corpus.Documents[0].GetMatchedLines(match).Should().HaveCount(LyricMatchConstants.MAX_MATCHED_LINES)
            .And.StartWith("Take me out with the crowd;");
    }

    [Fact]
    public void Score_UnrelatedLyrics_StaysBelowAmbiguousScore()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME, UNRELATED);

        LyricMatch match = corpus.Score("buy me some peanuts and cracker jack", 1);

        match.Score.Should().BeLessThan(LyricMatchConstants.AMBIGUOUS_SCORE);
    }

    [Theory]
    [InlineData("i dont care if i never get back")]
    [InlineData("I DON'T care if I never get back!")]
    [InlineData("i don’t care if i never get back")]
    public void Score_ContractionsAndCaseDoNotMatter(string fragment)
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME);

        corpus.Score(fragment, 0).Score.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Score_TyposStillAlignAsFuzzyMatches()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME, UNRELATED);

        LyricMatch match = corpus.Score("buy me some peanust and craker jack", 0);

        match.Align.Should().BeGreaterThan(0.85);
        match.Score.Should().BeGreaterThanOrEqualTo(LyricMatchConstants.ACCEPT_SCORE);
    }

    [Fact]
    public void Score_CjkFragment_MatchesPerCharacter()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(JING_YE_SI, UNRELATED);

        LyricMatch exact = corpus.Score("举头望明月低头思故乡", 0);
        LyricMatch oneWrong = corpus.Score("举头望明月低头思家乡", 0);
        LyricMatch unrelated = corpus.Score("举头望明月低头思故乡", 1);

        exact.Score.Should().BeApproximately(1.0, 0.001);
        corpus.Documents[0].GetMatchedLines(exact).Should().Equal("举头望明月，", "低头思故乡。");
        oneWrong.Score.Should().BeGreaterThanOrEqualTo(LyricMatchConstants.ACCEPT_SCORE);
        unrelated.Score.Should().Be(0);
    }

    [Fact]
    public void ScoreText_ExternalExcerptWithUnknownWords_ScoresLikeLocalText()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(UNRELATED);
        LyricFragmentScorer scorer = corpus.CreateScorer("buy me some peanuts and cracker jack");

        LyricTextMatch external = scorer.ScoreText(BALL_GAME);

        external.Match.Score.Should().BeApproximately(1.0, 0.001);
        external.MatchedLines.Should().Equal("Buy me some peanuts and Cracker Jack,");
        external.Tokens.Should().Contain("peanuts");
    }

    [Fact]
    public void Score_BelowMinimumDocuments_WeighsEveryWordTheSame()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build("alpha beta", "alpha gamma");

        LyricMatch match = corpus.Score("alpha beta delta", 1);

        match.IdfBagOfWords.Should().BeApproximately(1.0 / 3.0, 0.0001);
    }

    [Fact]
    public void Score_AtMinimumDocuments_WeighsRareWordsMore()
    {
        List<string> lyrics = Enumerable
            .Range(0, LyricMatchConstants.IDF_MIN_DOCUMENTS)
            .Select(i => $"common words here line {i}")
            .ToList();
        lyrics.Add("common words zebra");
        SearchTestCorpus corpus = SearchTestCorpus.Build(lyrics);
        LyricFragmentScorer scorer = corpus.CreateScorer("common words zebra");

        double rareHit = scorer.Score(corpus.Documents[^1]).IdfBagOfWords;
        double commonOnly = scorer.Score(corpus.Documents[0]).IdfBagOfWords;

        rareHit.Should().BeApproximately(1.0, 0.0001);
        commonOnly.Should().BeLessThan(0.1);
    }

    [Fact]
    public void Score_UnknownFragmentWord_TakesTheIdfOfItsMostFrequentFuzzyMatch()
    {
        List<string> lyrics = Enumerable
            .Range(0, LyricMatchConstants.IDF_MIN_DOCUMENTS + 10)
            .Select(i => i == 0 ? "alpha beta" : $"alpha beta gamma {i}")
            .ToList();
        SearchTestCorpus corpus = SearchTestCorpus.Build(lyrics);

        LyricMatch match = corpus.Score("alpha beta gamam", 0);

        // Without the fallback the typo would take the maximum idf and outweigh the two hits.
        match.IdfBagOfWords.Should().BeGreaterThan(0.3);
    }

    [Fact]
    public void Score_EmptyDocument_IsNone()
    {
        SearchTestCorpus corpus = SearchTestCorpus.Build(BALL_GAME, string.Empty);

        corpus.Score("buy me some peanuts", 1).Should().Be(LyricMatch.None);
    }

    [Fact]
    public void Constructor_InvalidFragment_Throws()
    {
        Action act = () => _ = new LyricFragmentScorer(LyricTextNormalizer.PrepareFragment("hi"), LyricTermTable.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
