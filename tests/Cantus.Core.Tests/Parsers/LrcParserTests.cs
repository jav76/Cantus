using Cantus.Core.Parsers;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Parsers;

public class LrcParserTests
{
    [Fact]
    public void Parse_WhenRawLrcIsEmpty_ReturnsEmptySyncedLyrics()
    {
        var result = LrcParser.Parse(string.Empty, "track1", "Title", "Artist");

        result.Should().NotBeNull();
        result.Lines.Should().BeEmpty();
        result.IsSynced.Should().BeFalse();
        result.TrackId.Should().Be("track1");
    }

    [Fact]
    public void Parse_WithStandardTwoDigitCentiseconds_ParsesCorrectTimestamps()
    {
        string lrc = """
            [00:12.50]First line of song
            [01:05.20]Second line of song
            [02:30.00]Third line of song
            """;

        var result = LrcParser.Parse(lrc, "t1", "Title", "Artist");

        result.Lines.Should().HaveCount(3);
        result.IsSynced.Should().BeTrue();

        result.Lines[0].Timestamp.Should().Be(TimeSpan.FromSeconds(12.5));
        result.Lines[0].Text.Should().Be("First line of song");

        result.Lines[1].Timestamp.Should().Be(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(5.2));
        result.Lines[1].Text.Should().Be("Second line of song");

        result.Lines[2].Timestamp.Should().Be(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));
        result.Lines[2].Text.Should().Be("Third line of song");
    }

    [Fact]
    public void Parse_WithThreeDigitMilliseconds_ParsesCorrectly()
    {
        string lrc = "[00:04.123]High precision line";

        var result = LrcParser.Parse(lrc);

        result.Lines.Should().ContainSingle();
        result.Lines[0].Timestamp.Should().Be(TimeSpan.FromMilliseconds(4123));
        result.Lines[0].Text.Should().Be("High precision line");
    }

    [Fact]
    public void Parse_WithMultipleTimestampsOnSingleLine_ExpandsAndSortsLines()
    {
        string lrc = """
            [00:10.00][00:30.00]Repeated chorus line
            [00:20.00]Middle verse
            """;

        var result = LrcParser.Parse(lrc);

        result.Lines.Should().HaveCount(3);
        result.Lines[0].Timestamp.Should().Be(TimeSpan.FromSeconds(10));
        result.Lines[0].Text.Should().Be("Repeated chorus line");

        result.Lines[1].Timestamp.Should().Be(TimeSpan.FromSeconds(20));
        result.Lines[1].Text.Should().Be("Middle verse");

        result.Lines[2].Timestamp.Should().Be(TimeSpan.FromSeconds(30));
        result.Lines[2].Text.Should().Be("Repeated chorus line");
    }

    [Fact]
    public void Parse_WithMetadataTags_IgnoresMetadataAndParsesLyrics()
    {
        string lrc = """
            [ti:Test Title]
            [ar:Test Artist]
            [al:Test Album]
            [length:03:30]
            [00:05.00]Actual lyric line
            """;

        var result = LrcParser.Parse(lrc);

        result.Lines.Should().ContainSingle();
        result.Lines[0].Timestamp.Should().Be(TimeSpan.FromSeconds(5));
        result.Lines[0].Text.Should().Be("Actual lyric line");
    }

    [Fact]
    public void Parse_WithInlineSyllables_ParsesSyllablesAndCleanText()
    {
        string lrc = "[00:10.00]<00:10.00>Never <00:10.50>gonna <00:11.00>give <00:11.50>you <00:12.00>up";

        var result = LrcParser.Parse(lrc);

        result.Lines.Should().ContainSingle();
        var line = result.Lines[0];
        line.Text.Should().Be("Never gonna give you up");
        line.Syllables.Should().NotBeNull();
        line.Syllables.Should().HaveCount(5);
        line.Syllables![0].Text.Should().Be("Never");
        line.Syllables[0].Timestamp.Should().Be(TimeSpan.FromSeconds(10));
        line.Syllables[1].Text.Should().Be("gonna");
        line.Syllables[1].Timestamp.Should().Be(TimeSpan.FromSeconds(10.5));
    }

    [Fact]
    public void GetWordTimestamps_WithoutSyllables_EstimatesProportionalTimestamps()
    {
        var line = new Cantus.Core.Models.LyricLine(TimeSpan.FromSeconds(10), "Never gonna give you up");

        var words = line.GetWordTimestamps(TimeSpan.FromSeconds(5));

        words.Should().HaveCount(5);
        words[0].Word.Should().Be("Never");
        words[0].Timestamp.Should().Be(TimeSpan.FromSeconds(10));
        words[^1].Word.Should().Be("up");
        words[^1].Timestamp.Should().BeGreaterThan(TimeSpan.FromSeconds(10));
        words[^1].Timestamp.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(15));
    }
}

