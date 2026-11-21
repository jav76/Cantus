using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public sealed class SpotifyTrackMatcherTests
{
    private const int SONG_DURATION_MS = 354_000;
    private const int WITHIN_TOLERANCE_MS = 2_500;
    private const int OUTSIDE_TOLERANCE_MS = 4_000;

    private static TrackInfo Track(string id, string title, string artist, int durationMs = SONG_DURATION_MS)
    {
        return new TrackInfo
        {
            Id = id,
            Title = title,
            Artist = artist,
            Duration = TimeSpan.FromMilliseconds(durationMs),
        };
    }

    [Fact]
    public void SelectBest_WithSameTitleAndArtist_AcceptsTheTrack()
    {
        TrackInfo track = Track("a", "Bohemian Rhapsody", "Queen");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Bohemian Rhapsody", "Queen", null);

        match.Should().BeSameAs(track);
    }

    [Fact]
    public void SelectBest_IgnoresRemasterQualifiersAndCaseAndDiacritics()
    {
        TrackInfo track = Track("a", "Bohemian Rhapsody - Remastered 2011", "Queen");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "bohemian rhapsody", "QUEEN", null);

        match.Should().BeSameAs(track);
    }

    [Fact]
    public void SelectBest_WhenTheTrackTitleContainsTheGuessTitle_AcceptsIt()
    {
        TrackInfo track = Track("a", "Hallelujah (from Shrek)", "Rufus Wainwright");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Hallelujah", "Rufus Wainwright", null);

        match.Should().BeSameAs(track);
    }

    [Fact]
    public void SelectBest_WithADifferentTitle_RejectsEvenWhenTheArtistMatches()
    {
        TrackInfo track = Track("a", "Somebody to Love", "Queen");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Bohemian Rhapsody", "Queen", SONG_DURATION_MS);

        match.Should().BeNull();
    }

    [Fact]
    public void SelectBest_WithTheTitleOnlyAsPartOfAWord_Rejects()
    {
        TrackInfo track = Track("a", "Lovesong", "The Cure");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Love", "The Cure", null);

        match.Should().BeNull();
    }

    [Fact]
    public void SelectBest_WithAnotherArtistAndNoDuration_Rejects()
    {
        TrackInfo cover = Track("a", "Hallelujah", "Pentatonix");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { cover }, "Hallelujah", "Leonard Cohen", null);

        match.Should().BeNull();
    }

    [Fact]
    public void SelectBest_WithAnArtistOnlyMatchingPartOfAWord_Rejects()
    {
        TrackInfo track = Track("a", "Yesterday", "Beatlemania Tribute");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Yesterday", "Beatle", null);

        match.Should().BeNull();
    }

    [Fact]
    public void SelectBest_MatchesAnyCreditedArtistOnWholeWords()
    {
        TrackInfo track = Track("a", "Under Pressure", "Queen, David Bowie");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Under Pressure", "David Bowie", null);

        match.Should().BeSameAs(track);
    }

    [Fact]
    public void SelectBest_WithAnArtistInAnotherScript_AcceptsWhenTheDurationIsWithinTolerance()
    {
        TrackInfo track = Track("a", "Nocturne", "Jay Chou", SONG_DURATION_MS + WITHIN_TOLERANCE_MS);

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Nocturne", "周杰伦", SONG_DURATION_MS);

        match.Should().BeSameAs(track);
    }

    [Fact]
    public void SelectBest_WithAnArtistInAnotherScript_RejectsWhenTheDurationIsOutsideTolerance()
    {
        TrackInfo track = Track("a", "Nocturne", "Jay Chou", SONG_DURATION_MS + OUTSIDE_TOLERANCE_MS);

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { track }, "Nocturne", "周杰伦", SONG_DURATION_MS);

        match.Should().BeNull();
    }

    [Fact]
    public void SelectBest_WithSeveralAcceptedTracks_PrefersTheClosestDuration()
    {
        TrackInfo far = Track("far", "Bohemian Rhapsody", "Queen", SONG_DURATION_MS + OUTSIDE_TOLERANCE_MS);
        TrackInfo close = Track("close", "Bohemian Rhapsody", "Queen", SONG_DURATION_MS + WITHIN_TOLERANCE_MS);

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { far, close }, "Bohemian Rhapsody", "Queen", SONG_DURATION_MS);

        match.Should().BeSameAs(close);
    }

    [Fact]
    public void SelectBest_PrefersAnExactTitleOverAContainedOne()
    {
        TrackInfo contained = Track("contained", "Hallelujah (from Shrek)", "Rufus Wainwright");
        TrackInfo exact = Track("exact", "Hallelujah", "Rufus Wainwright");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { contained, exact }, "Hallelujah", "Rufus Wainwright", null);

        match.Should().BeSameAs(exact);
    }

    [Fact]
    public void SelectBest_WithoutADurationAndEqualRank_KeepsSpotifysOrder()
    {
        TrackInfo first = Track("first", "Yesterday", "The Beatles");
        TrackInfo second = Track("second", "Yesterday - Remastered 2009", "The Beatles");

        TrackInfo? match = SpotifyTrackMatcher.SelectBest(new[] { first, second }, "Yesterday", "The Beatles", null);

        match.Should().BeSameAs(first);
    }

    [Fact]
    public void SelectBest_WithNoTracksOrNoTitle_ReturnsNull()
    {
        SpotifyTrackMatcher.SelectBest(Array.Empty<TrackInfo>(), "Yesterday", "The Beatles", null).Should().BeNull();
        SpotifyTrackMatcher.SelectBest(new[] { Track("a", "Yesterday", "The Beatles") }, " ", "The Beatles", null)
            .Should().BeNull();
    }
}
