using Cantus.Core.Search;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Search;

public class LyricTextNormalizerTests
{
    [Theory]
    [InlineData("don't stop", "dont stop")]
    [InlineData("don’t stop", "dont stop")]
    [InlineData("‘twas the night", "twas the night")]
    [InlineData("donʼt stop", "dont stop")]
    public void Tokenize_DeletesApostrophesWithoutSplitting(string input, string expected)
    {
        string.Join(' ', LyricTextNormalizer.Tokenize(input)).Should().Be(expected);
    }

    [Theory]
    [InlineData("Café Señor ça", "cafe senor ca")]
    [InlineData("Frère Jacques, dormez-vous?", "frere jacques dormez vous")]
    [InlineData("ＡＢＣ ﬁne", "abc fine")]
    [InlineData("Straße", "strasse")]
    [InlineData("Øresund Łódź Đorđe", "oresund lodz dorde")]
    [InlineData("Æther œuvre", "aether oeuvre")]
    public void Tokenize_FoldsDiacriticsWidthLigaturesAndSpecialLetters(string input, string expected)
    {
        string.Join(' ', LyricTextNormalizer.Tokenize(input)).Should().Be(expected);
    }

    [Fact]
    public void Tokenize_PunctuationSeparatesWords()
    {
        LyricTextNormalizer.Tokenize("Root, root, root... for the home-team!")
            .Should().Equal("root", "root", "root", "for", "the", "home", "team");
    }

    [Fact]
    public void Tokenize_EachHanCharacterIsItsOwnToken()
    {
        LyricTextNormalizer.Tokenize("床前明月光，疑是地上霜")
            .Should().Equal("床", "前", "明", "月", "光", "疑", "是", "地", "上", "霜");
    }

    [Fact]
    public void Tokenize_PreservesVoicedKanaAndHangul()
    {
        LyricTextNormalizer.Tokenize("がパ").Should().Equal("が", "パ");
        LyricTextNormalizer.Tokenize("ﾊﾟ").Should().Equal("パ");
        LyricTextNormalizer.Tokenize("사랑해").Should().Equal("사", "랑", "해");
    }

    [Fact]
    public void Tokenize_KatakanaMiddleDotSeparatesAndIterationMarksAreCjkTokens()
    {
        LyricTextNormalizer.Tokenize("ロックン・ロール").Should().Equal("ロ", "ッ", "ク", "ン", "ロ", "ー", "ル");
        LyricTextNormalizer.Tokenize("時々").Should().Equal("時", "々");
        LyricTextNormalizer.IsCjkToken("々").Should().BeTrue();
        LyricTextNormalizer.IsCjkToken("〇").Should().BeTrue();
        LyricTextNormalizer.IsCjkToken("・").Should().BeFalse();
    }

    [Fact]
    public void Tokenize_KeepsCombiningMarksOutsideTheLatinRangeInsideWords()
    {
        LyricTextNormalizer.Tokenize("हिन्दी गाना").Should().HaveCount(2);
    }

    [Fact]
    public void Normalize_DropsSectionLabelsAndBlankLinesAndInlineTags()
    {
        string lyrics = """
            [Verse 1]
            First line here

            Chorus:
            Second [x2] line (backing vocals)
            Verse 2:
            [Chorus: Singer]
            Chorus: Singer
            Verse 3: Some Name
            Third line
            """;

        NormalizedLyricText result = LyricTextNormalizer.Normalize(lyrics);

        result.Lines.Should().Equal("First line here", "Second line (backing vocals)", "Third line");
        result.Tokens.Should().Equal("first", "line", "here", "second", "line", "backing", "vocals", "third", "line");
        result.LineStarts.Should().Equal(0, 3, 7);
    }

    [Fact]
    public void Normalize_KeepsLabelWordsThatAreLyrics()
    {
        NormalizedLyricText result = LyricTextNormalizer.Normalize("Chorus girls sing: la la");

        result.Tokens.Should().Equal("chorus", "girls", "sing", "la", "la");
    }

    [Fact]
    public void PrepareFragment_RejectsTooLongBeforeNormalizing()
    {
        string fragment = new('a', LyricMatchConstants.MAX_FRAGMENT_CHARACTERS + 1);

        LyricTextNormalizer.PrepareFragment(fragment).Status.Should().Be(LyricFragmentStatus.TooLong);
    }

    [Theory]
    [InlineData(null, LyricFragmentStatus.Empty)]
    [InlineData("  ...  ", LyricFragmentStatus.Empty)]
    [InlineData("two words", LyricFragmentStatus.TooShort)]
    [InlineData("three little words", LyricFragmentStatus.Valid)]
    [InlineData("明月光", LyricFragmentStatus.TooShort)]
    [InlineData("床前明月", LyricFragmentStatus.Valid)]
    public void PrepareFragment_AppliesPerScriptMinimums(string? fragment, LyricFragmentStatus expected)
    {
        LyricTextNormalizer.PrepareFragment(fragment).Status.Should().Be(expected);
    }

    [Fact]
    public void PrepareFragment_TruncatesWordsToTheCap()
    {
        string fragment = string.Join(' ', Enumerable.Range(0, 30).Select(i => $"w{i}"));

        LyricFragment result = LyricTextNormalizer.PrepareFragment(fragment);

        result.IsValid.Should().BeTrue();
        result.WasTruncated.Should().BeTrue();
        result.Tokens.Should().HaveCount(LyricMatchConstants.MAX_WORD_TOKENS);
        result.Tokens[^1].Should().Be("w23");
    }

    [Fact]
    public void PrepareFragment_AllowsUpToFortyEightCjkCharacters()
    {
        string fragment = new('月', 60);

        LyricFragment result = LyricTextNormalizer.PrepareFragment(fragment);

        result.IsValid.Should().BeTrue();
        result.WasTruncated.Should().BeTrue();
        result.CjkTokenCount.Should().Be(LyricMatchConstants.MAX_CJK_TOKENS);
    }

    [Fact]
    public void PrepareFragment_KeepsLineStartsForStitchedFragments()
    {
        LyricFragment result = LyricTextNormalizer.PrepareFragment("never get back\nlet me root");

        result.Text.LineStarts.Should().Equal(0, 3);
        result.ToSearchText().Should().Be("never get back let me root");
    }

    [Theory]
    [InlineData("Daisy Bell - 2011 Remaster", "daisy bell")]
    [InlineData("Daisy Bell (Remastered 2011)", "daisy bell")]
    [InlineData("Shine (Live at Wembley)", "shine")]
    [InlineData("Shine (feat. Somebody)", "shine")]
    [InlineData("Shine [Radio Edit]", "shine")]
    [InlineData("Daisy Bell (Bicycle Built for Two)", "daisy bell bicycle built for two")]
    [InlineData("Take Me Out - To The Ball Game", "take me out to the ball game")]
    [InlineData("Live", "live")]
    public void TitleCore_StripsVersionQualifiersOnly(string title, string expected)
    {
        LyricTextNormalizer.TitleCore(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("Simon & Garfunkel", "simon")]
    [InlineData("Artist A, Artist B", "artist a")]
    [InlineData("Artist A feat. Artist B", "artist a")]
    [InlineData("Artist A x Artist B", "artist a")]
    [InlineData("Beyoncé", "beyonce")]
    public void ArtistKey_TakesTheFirstCreditedArtist(string artist, string expected)
    {
        LyricTextNormalizer.ArtistKey(artist).Should().Be(expected);
    }

    [Fact]
    public void JoinTokens_DoesNotSpaceCjkCharacters()
    {
        LyricTextNormalizer.JoinTokens(["明", "月", "moon", "光"]).Should().Be("明月 moon 光");
    }
}
