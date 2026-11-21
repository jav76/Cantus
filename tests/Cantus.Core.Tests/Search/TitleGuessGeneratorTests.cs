using Cantus.Core.Search;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Search;

public class TitleGuessGeneratorTests
{
    [Fact]
    public void Generate_PrefersTheStartOfTheFragmentAndSkipsOverlaps()
    {
        string[] tokens = LyricTextNormalizer.Tokenize("take me out to the ball game");

        IReadOnlyList<string> guesses = TitleGuessGenerator.Generate(tokens, 2);

        guesses.Should().Equal("take me out", "ball game");
    }

    [Fact]
    public void Generate_SkipsStopwordOnlyWindows()
    {
        string[] tokens = LyricTextNormalizer.Tokenize("oh and you and me");

        TitleGuessGenerator.Generate(tokens, 3).Should().BeEmpty();
    }

    [Fact]
    public void Generate_RespectsTheGuessCapAndWindowLengths()
    {
        string[] tokens = LyricTextNormalizer.Tokenize("silver moon rising over quiet harbour lights tonight forever");

        IReadOnlyList<string> guesses = TitleGuessGenerator.Generate(tokens, 2);

        guesses.Should().HaveCount(2);
        guesses.Should().OnlyContain(g =>
            g.Split(' ').Length >= TitleGuessGenerator.MIN_WINDOW_TOKENS
            && g.Split(' ').Length <= TitleGuessGenerator.MAX_WINDOW_TOKENS);
    }

    [Fact]
    public void Generate_RareWordsRankAboveCommonOnesWhenIdfIsAvailable()
    {
        LyricTermTableBuilder builder = new();
        for (int i = 0; i < LyricMatchConstants.IDF_MIN_DOCUMENTS; i++)
        {
            builder.AddDocument(LyricTextNormalizer.Normalize("baby love tonight"));
        }

        builder.AddDocument(LyricTextNormalizer.Normalize("shenandoah river"));
        string[] tokens = LyricTextNormalizer.Tokenize("baby love tonight we cross shenandoah river");

        IReadOnlyList<string> guesses = TitleGuessGenerator.Generate(tokens, 1, builder.Build());

        guesses.Should().ContainSingle().Which.Should().Contain("shenandoah");
    }

    [Fact]
    public void Generate_JoinsCjkCharactersWithoutSpaces()
    {
        string[] tokens = LyricTextNormalizer.Tokenize("床前明月光");

        TitleGuessGenerator.Generate(tokens, 1).Should().Equal("床前明月");
    }

    [Fact]
    public void Generate_TooFewTokensOrNoGuesses_ReturnsEmpty()
    {
        TitleGuessGenerator.Generate(["alone"], 2).Should().BeEmpty();
        TitleGuessGenerator.Generate(["two", "words"], 0).Should().BeEmpty();
    }
}
