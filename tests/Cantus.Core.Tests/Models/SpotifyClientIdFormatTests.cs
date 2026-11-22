using Cantus.Core.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Models;

public sealed class SpotifyClientIdFormatTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef")]
    [InlineData("  0123456789ABCDEF0123456789ABCDEF  ", "0123456789abcdef0123456789abcdef")]
    public void TryNormalize_AcceptsAndLowercasesA32CharacterHexId(string input, string expected)
    {
        SpotifyClientIdFormat.TryNormalize(input, out string? clientId).Should().BeTrue();
        clientId.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("your_32_character_client_id")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void TryNormalize_RejectsAnythingElse(string? input)
    {
        SpotifyClientIdFormat.TryNormalize(input, out string? clientId).Should().BeFalse();
        clientId.Should().BeNull();
    }
}
