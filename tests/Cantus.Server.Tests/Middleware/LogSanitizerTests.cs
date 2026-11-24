using Cantus.Server.Middleware;
using FluentAssertions;
using Xunit;

namespace Cantus.Server.Tests.Middleware;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("/api/test", "/api/test")]
    [InlineData("?q=café 歌", "?q=café 歌")]
    [InlineData("/a\nFATAL forged", "/a FATAL forged")]
    [InlineData("/a\r\nforged", "/a  forged")]
    [InlineData("/a\tb\0c\u001bd", "/a b c d")]
    [InlineData("/a\u0085b\u2028c\u2029d", "/a b c d")]
    public void SingleLine_ReplacesLineBreaksAndControlCharactersWithSpaces(string input, string expected)
    {
        LogSanitizer.SingleLine(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SingleLine_ReturnsEmptyForNullOrEmpty(string? input)
    {
        LogSanitizer.SingleLine(input).Should().BeEmpty();
    }
}
