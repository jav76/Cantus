using Cantus.Core.Search;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Search;

public class LyricTokenHelpersTests
{
    [Theory]
    [InlineData("running", "run")]
    [InlineData("loving", "lov")]
    [InlineData("loved", "lov")]
    [InlineData("cries", "cry")]
    [InlineData("cried", "cry")]
    [InlineData("nothin", "noth")]
    [InlineData("lovin", "lovin")]
    [InlineData("knowingly", "know")]
    [InlineData("dreams", "dream")]
    [InlineData("is", "is")]
    [InlineData("bring", "bring")]
    [InlineData("shine", "shin")]
    [InlineData("love", "love")]
    public void Stem_StripsLightSuffixesAndKeepsThreeCharacters(string word, string expected)
    {
        LightStemmer.Stem(word).Should().Be(expected);
    }

    [Theory]
    [InlineData("night", "knight")]
    [InlineData("phone", "fone")]
    [InlineData("write", "rite")]
    [InlineData("city", "sity")]
    [InlineData("photo", "foto")]
    public void PhoneticKey_EqualForSoundAlikes(string a, string b)
    {
        PhoneticKey.Compute(a).Should().NotBeNull().And.Be(PhoneticKey.Compute(b));
    }

    [Theory]
    [InlineData("me")]
    [InlineData("aye")]
    [InlineData("123")]
    public void PhoneticKey_NullForShortTokensOrKeys(string token)
    {
        PhoneticKey.Compute(token).Should().BeNull();
    }

    [Theory]
    [InlineData("love", "lvoe", true)]
    [InlineData("love", "lover", true)]
    [InlineData("love", "lovers", false)]
    [InlineData("cat", "car", false)]
    [InlineData("beautiful", "beutifull", true)]
    [InlineData("beautiful", "butiful", false)]
    [InlineData("beautiful", "btfl", false)]
    public void IsFuzzyMatch_AppliesLengthGuards(string a, string b, bool expected)
    {
        OsaDistance.IsFuzzyMatch(a, b).Should().Be(expected);
    }

    [Fact]
    public void IsWithin_CountsATranspositionAsOneEdit()
    {
        OsaDistance.IsWithin("abcd", "abdc", 1).Should().BeTrue();
        OsaDistance.IsWithin("abcd", "badc", 1).Should().BeFalse();
        OsaDistance.IsWithin("abcd", "badc", 2).Should().BeTrue();
    }
}
