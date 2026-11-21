using Cantus.Core.Logging;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Logging;

public sealed class SessionLogRefTests
{
    [Fact]
    public void For_ReturnsShortLowercaseHexThatDoesNotContainTheId()
    {
        string sessionId = "0123456789abcdef0123456789abcdef";

        string reference = SessionLogRef.For(sessionId);

        reference.Should().MatchRegex("^[0-9a-f]{12}$");
        sessionId.Should().NotContain(reference);
    }

    [Fact]
    public void For_IsStableForTheSameIdAndDiffersBetweenIds()
    {
        SessionLogRef.For("session-a").Should().Be(SessionLogRef.For("session-a"));
        SessionLogRef.For("session-a").Should().NotBe(SessionLogRef.For("session-b"));
    }

    [Fact]
    public void For_IsTheSha256PrefixOfTheId()
    {
        // SHA-256("abc") starts with ba7816bf8f01.
        SessionLogRef.For("abc").Should().Be("ba7816bf8f01");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void For_WithoutAnId_ReturnsNone(string? sessionId)
    {
        SessionLogRef.For(sessionId).Should().Be(SessionLogRef.NONE);
    }
}
