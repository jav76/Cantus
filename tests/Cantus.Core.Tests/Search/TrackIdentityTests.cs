using Cantus.Core.Search;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Search;

public sealed class TrackIdentityTests
{
    private const string TITLE = "Glimpse of Us";
    private const string ARTIST = "Joji";
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(233_000);

    [Theory]
    [InlineData("2mlNgAeIBnL78ZriXgrRHz")]
    [InlineData("0000000000000000000000")]
    public void TryParseSpotifyTrackId_WithTwentyTwoBase62Characters_Succeeds(string value)
    {
        TrackIdentity.TryParseSpotifyTrackId(value, out string? trackId).Should().BeTrue();
        trackId.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2mlNgAeIBnL78ZriXgrRH")]
    [InlineData("2mlNgAeIBnL78ZriXgrRHzz")]
    [InlineData("2mlNgAeIBnL78ZriXgrRH-")]
    [InlineData("spotify:track:2mlNgAeIBnL78ZriXgrRHz")]
    [InlineData("2mlNgAeIBnL78ZriXgrRHz\n")]
    [InlineData("local:0123456789abcdef0123456789abcdef")]
    public void TryParseSpotifyTrackId_WithAnythingElse_Fails(string? value)
    {
        TrackIdentity.TryParseSpotifyTrackId(value, out string? trackId).Should().BeFalse();
        trackId.Should().BeNull();
    }

    [Fact]
    public void DeriveLocalKey_IsLocalPrefixPlus32LowercaseHexCharacters()
    {
        string key = TrackIdentity.DeriveLocalKey(TITLE, ARTIST, Duration);

        key.Should().MatchRegex("^local:[0-9a-f]{32}$");
        TrackIdentity.IsLocalKey(key).Should().BeTrue();
    }

    [Fact]
    public void DeriveLocalKey_IsStableAcrossCalls()
    {
        TrackIdentity.DeriveLocalKey(TITLE, ARTIST, Duration)
            .Should().Be(TrackIdentity.DeriveLocalKey(TITLE, ARTIST, Duration));
    }

    [Theory]
    [InlineData("glimpse of us", "joji")]
    [InlineData("GLIMPSE OF US", "JOJI")]
    [InlineData("  Glimpse   of  Us ", " Joji ")]
    [InlineData("Glimpse, of Us!", "Joji")]
    [InlineData("Glimpse of Us", "Jöji")]
    [InlineData("Glimpse of Us", "Joji feat. Someone Else")]
    [InlineData("Glimpse of Us", "Joji, Another Artist")]
    public void DeriveLocalKey_FoldsCaseWhitespacePunctuationAccentsAndFeaturedArtists(string title, string artist)
    {
        TrackIdentity.DeriveLocalKey(title, artist, Duration)
            .Should().Be(TrackIdentity.DeriveLocalKey(TITLE, ARTIST, Duration));
    }

    [Theory]
    [InlineData(232_500)]
    [InlineData(233_000)]
    [InlineData(233_499)]
    public void DeriveLocalKey_RoundsDurationToWholeSeconds(long durationMs)
    {
        TrackIdentity.DeriveLocalKey(TITLE, ARTIST, TimeSpan.FromMilliseconds(durationMs))
            .Should().Be(TrackIdentity.DeriveLocalKey(TITLE, ARTIST, Duration));
    }

    [Theory]
    [InlineData(232_499)]
    [InlineData(233_500)]
    [InlineData(180_000)]
    public void DeriveLocalKey_WithADifferentDuration_GivesADifferentKey(long durationMs)
    {
        TrackIdentity.DeriveLocalKey(TITLE, ARTIST, TimeSpan.FromMilliseconds(durationMs))
            .Should().NotBe(TrackIdentity.DeriveLocalKey(TITLE, ARTIST, Duration));
    }

    [Theory]
    [InlineData("Glimpse of Us (Remastered)", ARTIST)]
    [InlineData("Glimpse of Them", ARTIST)]
    [InlineData(TITLE, "Someone Else")]
    public void DeriveLocalKey_WithADifferentTitleOrArtist_GivesADifferentKey(string title, string artist)
    {
        TrackIdentity.DeriveLocalKey(title, artist, Duration)
            .Should().NotBe(TrackIdentity.DeriveLocalKey(TITLE, ARTIST, Duration));
    }

    [Fact]
    public void DeriveLocalKey_KeepsFieldsApart()
    {
        TrackIdentity.DeriveLocalKey("b c", "a", Duration)
            .Should().NotBe(TrackIdentity.DeriveLocalKey("c", "a b", Duration));
    }

    [Fact]
    public void DeriveLocalKey_WithPunctuationOnlyNames_StillGivesDistinctKeys()
    {
        TrackIdentity.DeriveLocalKey("!!!", "???", Duration)
            .Should().NotBe(TrackIdentity.DeriveLocalKey("...", "???", Duration));
    }
}
