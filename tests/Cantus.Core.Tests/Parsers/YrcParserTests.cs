using System.Text.Json;
using Cantus.Core.Models;
using Cantus.Core.Parsers;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Parsers;

public class YrcParserTests
{
    private static string LoadYrcFixture(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Netease", fileName);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("yrc").GetProperty("lyric").GetString()!;
    }

    private static int CountNonBlankLines(string raw)
    {
        return raw
            .Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n")]
    public void ParseLines_WhenInputIsBlank_ReturnsEmpty(string? raw)
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        lines.Should().BeEmpty();
    }

    [Fact]
    public void ParseLines_WithWordTokens_MapsEachTokenToASyllableWithDuration()
    {
        string raw = "[27360,1290](27360,240,0)alpha (27600,90,0)bravo (27690,360,0)charlie (28050,600,0)delta";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Timestamp.Should().Be(TimeSpan.FromMilliseconds(27360));
        line.Text.Should().Be("alpha bravo charlie delta");
        line.Syllables.Should().Equal(
            new LyricSyllable(TimeSpan.FromMilliseconds(27360), TimeSpan.FromMilliseconds(240), "alpha "),
            new LyricSyllable(TimeSpan.FromMilliseconds(27600), TimeSpan.FromMilliseconds(90), "bravo "),
            new LyricSyllable(TimeSpan.FromMilliseconds(27690), TimeSpan.FromMilliseconds(360), "charlie "),
            new LyricSyllable(TimeSpan.FromMilliseconds(28050), TimeSpan.FromMilliseconds(600), "delta"));
    }

    [Fact]
    public void ParseLines_WithSplitWordAndPunctuationTokens_KeepsTokenTextAsIs()
    {
        string raw = "[32820,1080](32820,540,0)echo(33360,120,0)fox (33480,180,0)golf(33660,0,0), (33660,240,0)hotel";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Text.Should().Be("echofox golf, hotel");
        line.Syllables!.Select(s => s.Text).Should().Equal("echo", "fox ", "golf", ", ", "hotel");
        line.Syllables![3].Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ParseLines_WithCjkTokens_JoinsCharactersWithoutSpaces()
    {
        string raw = "[40000,1270](40000,890,0)甲(40890,380,0)乙";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Text.Should().Be("甲乙");
        line.Syllables.Should().HaveCount(2);
        line.Syllables![1].Timestamp.Should().Be(TimeSpan.FromMilliseconds(40890));
        line.Syllables![1].Duration.Should().Be(TimeSpan.FromMilliseconds(380));
    }

    [Fact]
    public void ParseLines_WithCreditHeaderAndBlankSeparator_KeepsCreditsAsLinesAndSkipsBlank()
    {
        string raw = "[0,1000](0,1000,0) 作词 : Name One\n" +
            "[1000,1000](1000,1000,0) 作曲 : Name Two\n" +
            "\n" +
            "[27360,1290](27360,240,0)alpha (27600,90,0)bravo\n";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        lines.Select(l => l.Text).Should().Equal("作词 : Name One", "作曲 : Name Two", "alpha bravo");
        lines.Select(l => l.Timestamp.TotalMilliseconds).Should().Equal(0, 1000, 27360);
    }

    [Theory]
    [InlineData("word-timed-latin.json")]
    [InlineData("word-timed-cjk.json")]
    [InlineData("word-timed-credit-block.json")]
    [InlineData("word-timed-stretched-credit.json")]
    public void ParseLines_WithFixture_ProducesOneSyllableLinePerNonBlankLine(string fileName)
    {
        string raw = LoadYrcFixture(fileName);

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        lines.Should().HaveCount(CountNonBlankLines(raw));
        lines.Select(l => l.Timestamp).Should().BeInAscendingOrder();
        foreach (LyricLine line in lines)
        {
            line.Syllables.Should().NotBeNullOrEmpty();
            line.Syllables!.Select(s => s.Timestamp).Should().BeInAscendingOrder();
            line.Syllables![0].Timestamp.Should().BeGreaterThanOrEqualTo(line.Timestamp);
            line.Text.Should().Be(string.Concat(line.Syllables!.Select(s => s.Text)).Trim());
        }
    }

    [Fact]
    public void ParseLines_WithLatinFixture_ReadsSyllableTimingAndSplitWords()
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(LoadYrcFixture("word-timed-latin.json"));

        LyricLine line = lines.Single(l => l.Timestamp == TimeSpan.FromMilliseconds(32820));
        line.Text.Should().Be(
            "lima5 mike5 november5 oscar5 papa5 quebec5romeo5 sierra5, tango5");
        line.Syllables.Should().HaveCount(10);
        line.Syllables![8].Should().Be(
            new LyricSyllable(TimeSpan.FromMilliseconds(35640), TimeSpan.FromMilliseconds(30), ", "));
        line.Syllables![9].Should().Be(
            new LyricSyllable(TimeSpan.FromMilliseconds(35670), TimeSpan.FromMilliseconds(1950), "tango5"));
    }

    [Fact]
    public void ParseLines_WithCjkFixture_ReadsOneSyllablePerCharacter()
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(LoadYrcFixture("word-timed-cjk.json"));

        LyricLine line = lines.Single(l => l.Timestamp == TimeSpan.FromMilliseconds(7400));
        line.Text.Should().Be("甲乙丙丁戊己庚");
        line.Syllables.Should().HaveCount(line.Text.Length);
        line.Syllables![6].Should().Be(
            new LyricSyllable(TimeSpan.FromMilliseconds(11710), TimeSpan.FromMilliseconds(3240), "庚"));
    }

    [Fact]
    public void ParseLines_WithCreditBlockFixture_JoinsPerCharacterCreditText()
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(LoadYrcFixture("word-timed-credit-block.json"));

        lines.Should().Contain(l => l.Text == "作词 : 甲乙" && l.Timestamp == TimeSpan.Zero);
        LyricLine perCharacterCredit = lines.Single(l => l.Timestamp == TimeSpan.FromMilliseconds(1));
        perCharacterCredit.Text.Should().StartWith("词版权管理方：");
        perCharacterCredit.Syllables![0].Text.Should().Be("词");
    }

    [Fact]
    public void ParseLines_WithStretchedCreditFixture_KeepsLongCreditSyllables()
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(LoadYrcFixture("word-timed-stretched-credit.json"));

        LyricLine credit = lines.Single(l => l.Text.StartsWith("编曲", StringComparison.Ordinal));
        credit.Text.Should().Be("编曲：oscar2 papa2");
        credit.Syllables![0].Duration.Should().Be(TimeSpan.FromMilliseconds(10540));
    }

    [Theory]
    [InlineData("[1000,500](1000,abc,0)alpha (1200,100,0)bravo", "(1000,abc,0)alpha bravo")]
    [InlineData("[1000,500](1000,200)alpha (1200,100,0)bravo", "alpha bravo")]
    [InlineData("[1000,500](1000,200,0,9)alpha", "alpha")]
    [InlineData("[1000,500](1000,200,0alpha", "(1000,200,0alpha")]
    [InlineData("[1000,500](1000,200,0alpha (1200,100,0)bravo", "(1000,200,0alpha bravo")]
    [InlineData("[1000,500(1000,200,0)alpha [x]", "[1000,500alpha [x]")]
    [InlineData("[1000,500 (1000,200,0)alpha [x]", "[1000,500 alpha [x]")]
    [InlineData("[1000,500]plain words", "plain words")]
    [InlineData("[1000,500]lead (1000,200,0)alpha", "lead alpha")]
    [InlineData("[1000,500](1200,200,0)alpha (1000,100,0)bravo", "alpha bravo")]
    [InlineData("[1000,500](99999999999,200,0)alpha", "alpha")]
    [InlineData("[1000,500](-5,200,0)alpha", "(-5,200,0)alpha")]
    public void ParseLines_WithMalformedTokens_FallsBackToLineTiming(string raw, string expectedText)
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Timestamp.Should().Be(TimeSpan.FromMilliseconds(1000));
        line.Text.Should().Be(expectedText);
        line.Syllables.Should().BeNull();
    }

    [Theory]
    [InlineData("(1, 2, 3, 4)")]
    [InlineData("(1,2,3,4)")]
    [InlineData("(2,000 miles)")]
    [InlineData("(1,2)")]
    public void ParseLines_WithParenthesisedNumbersInTokenText_KeepsSyllablesAndText(string sungText)
    {
        string raw = $"[1000,500](1000,200,0)count (1200,100,0){sungText}";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Text.Should().Be($"count {sungText}");
        line.Syllables!.Select(s => s.Text).Should().Equal("count ", sungText);
    }

    [Fact]
    public void ParseLines_WithHeaderOnly_KeepsAnEmptyTimedLine()
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines("[1000,500]");

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Timestamp.Should().Be(TimeSpan.FromMilliseconds(1000));
        line.Text.Should().BeEmpty();
        line.Syllables.Should().BeNull();
    }

    [Fact]
    public void ParseLines_WithMalformedHeaderButValidTokens_UsesFirstTokenStart()
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines("[abc,500](1000,200,0)alpha (1200,100,0)bravo");

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Timestamp.Should().Be(TimeSpan.FromMilliseconds(1000));
        line.Text.Should().Be("alpha bravo");
        line.Syllables.Should().HaveCount(2);
    }

    [Fact]
    public void ParseLines_WithMissingHeaderAndValidTokens_UsesFirstTokenStart()
    {
        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines("(1000,200,0)alpha (1200,100,0)bravo");

        LyricLine line = lines.Should().ContainSingle().Subject;
        line.Timestamp.Should().Be(TimeSpan.FromMilliseconds(1000));
        line.Syllables.Should().HaveCount(2);
    }

    [Fact]
    public void ParseLines_WithUntimedLine_KeepsItAtThePreviousLineTimestamp()
    {
        string raw = "[1000,500](1000,500,0)alpha\n" +
            "no timing here\n" +
            "[2000,500](2000,500,0)bravo";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        lines.Select(l => l.Text).Should().Equal("alpha", "no timing here", "bravo");
        lines[1].Timestamp.Should().Be(TimeSpan.FromMilliseconds(1000));
        lines[1].Syllables.Should().BeNull();
    }

    [Fact]
    public void ParseLines_WithMalformedLineAmongValidOnes_KeepsSyllablesOnTheValidLines()
    {
        string raw = "[1000,500](1000,500,0)alpha\r\n" +
            "[2000,500](2000,x,0)bravo\r\n" +
            "[3000,500](3000,500,0)charlie\r\n";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        lines.Should().HaveCount(3);
        lines[0].Syllables.Should().ContainSingle();
        lines[1].Syllables.Should().BeNull();
        lines[1].Text.Should().Be("(2000,x,0)bravo");
        lines[2].Syllables.Should().ContainSingle().Which.Text.Should().Be("charlie");
    }

    [Fact]
    public void ParseLines_WithOutOfOrderLines_SortsByTimestamp()
    {
        string raw = "[2000,500](2000,500,0)bravo\n[1000,500](1000,500,0)alpha";

        IReadOnlyList<LyricLine> lines = YrcParser.ParseLines(raw);

        lines.Select(l => l.Text).Should().Equal("alpha", "bravo");
    }

    [Theory]
    [InlineData("word-timed-latin.json")]
    [InlineData("word-timed-cjk.json")]
    [InlineData("word-timed-credit-block.json")]
    [InlineData("word-timed-stretched-credit.json")]
    public void ParseLines_WithRandomlyCorruptedFixture_NeverThrowsAndNeverDropsALine(string fileName)
    {
        const int SEED = 66;
        const int ITERATIONS = 200;
        const int MAX_EDITS = 8;
        const string NOISE = "()[],0123456789ab -甲";

        string original = LoadYrcFixture(fileName);
        Random random = new(SEED);

        for (int iteration = 0; iteration < ITERATIONS; iteration++)
        {
            char[] corrupted = original.ToCharArray();
            int edits = random.Next(1, MAX_EDITS);
            for (int edit = 0; edit < edits; edit++)
            {
                int position = random.Next(corrupted.Length);
                if (corrupted[position] != '\n')
                {
                    corrupted[position] = NOISE[random.Next(NOISE.Length)];
                }
            }

            string raw = new(corrupted);
            Func<IReadOnlyList<LyricLine>> parse = () => YrcParser.ParseLines(raw);

            parse.Should().NotThrow().Subject.Should().HaveCount(CountNonBlankLines(raw));
        }
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("[,]")]
    [InlineData("[1,2")]
    [InlineData("(1,")]
    [InlineData("(1,2,3")]
    [InlineData("[1,2](")]
    [InlineData("[1,2]()")]
    [InlineData("[1,2](1,2,3)")]
    [InlineData("[1,2](1,2,3)(4,5,6)")]
    [InlineData("[99999999999999999999,1](1,2,3)x")]
    [InlineData("{\"t\":0,\"c\":[{\"tx\":\"label: \"}]}")]
    public void ParseLines_WithGarbageInput_NeverThrowsAndKeepsTheLine(string raw)
    {
        Func<IReadOnlyList<LyricLine>> parse = () => YrcParser.ParseLines(raw);

        parse.Should().NotThrow().Subject.Should().ContainSingle();
    }
}
