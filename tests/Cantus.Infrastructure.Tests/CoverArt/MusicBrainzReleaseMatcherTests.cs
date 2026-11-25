using Cantus.Core.Models;
using Cantus.Infrastructure.CoverArt;
using FluentAssertions;

namespace Cantus.Infrastructure.Tests.CoverArt;

public class MusicBrainzReleaseMatcherTests
{
    private const int MAX_GROUPS = 3;

    private static readonly Guid AlbumGroup = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SingleGroup = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CompilationGroup = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid OtherGroup = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly TimeSpan TrackLength = TimeSpan.FromSeconds(210);

    [Fact]
    public void SelectReleaseGroups_WhenTitleArtistAndLengthMatch_ReturnsTheStudioAlbum()
    {
        IReadOnlyList<Guid> groups = Select(
            Track(),
            Recording(releases: Album(AlbumGroup, "Jazz")));

        groups.Should().Equal(AlbumGroup);
    }

    [Theory]
    [InlineData("Another Song", "Queen", 210)]
    [InlineData("Don't Stop Me Now", "Someone Else", 210)]
    [InlineData("Don't Stop Me Now", "Queen", 216)]
    [InlineData("Don't Stop Me Now", "Queen", 204)]
    public void SelectReleaseGroups_WhenTitleArtistOrLengthDiffers_ReturnsNothing(
        string recordingTitle,
        string recordingArtist,
        int recordingSeconds)
    {
        IReadOnlyList<Guid> groups = Select(
            Track(),
            Recording(recordingTitle, recordingArtist, TimeSpan.FromSeconds(recordingSeconds), Album(AlbumGroup, "Jazz")));

        groups.Should().BeEmpty();
    }

    [Fact]
    public void SelectReleaseGroups_WhenTheLengthIsAtTheTolerance_Matches()
    {
        TimeSpan atTolerance = TrackLength + TimeSpan.FromSeconds(MusicBrainzReleaseMatcher.DURATION_TOLERANCE_SECONDS);

        IReadOnlyList<Guid> groups = Select(
            Track(),
            Recording(length: atTolerance, releases: Album(AlbumGroup, "Jazz")));

        groups.Should().Equal(AlbumGroup);
    }

    [Fact]
    public void SelectReleaseGroups_WhenTheRecordingHasNoLength_ReturnsNothing()
    {
        MusicBrainzRecording recording = Recording(releases: Album(AlbumGroup, "Jazz")) with { Length = null };

        Select(Track(), recording).Should().BeEmpty();
    }

    [Fact]
    public void SelectReleaseGroups_IgnoresQualifiersInThePlayersTitle()
    {
        IReadOnlyList<Guid> groups = Select(
            Track(title: "Don't Stop Me Now - Remastered 2011"),
            Recording(releases: Album(AlbumGroup, "Jazz")));

        groups.Should().Equal(AlbumGroup);
    }

    [Fact]
    public void SelectReleaseGroups_MatchesAnyCreditedArtist()
    {
        MusicBrainzRecording recording = new(
            "Under Pressure",
            ["Queen", "David Bowie"],
            TrackLength,
            [Album(AlbumGroup, "Hot Space")]);

        IReadOnlyList<Guid> groups = Select(Track(title: "Under Pressure", artist: "David Bowie"), recording);

        groups.Should().Equal(AlbumGroup);
    }

    [Theory]
    [InlineData("Official", "Album", "Compilation")]
    [InlineData("Official", "Album", "Live")]
    [InlineData("Bootleg", "Album", null)]
    [InlineData("Official", "Broadcast", null)]
    [InlineData(null, "Album", null)]
    public void SelectReleaseGroups_WithoutAnAlbumMatch_SkipsReleasesThatAreNotOfficialStudioReleases(
        string? status,
        string primaryType,
        string? secondaryType)
    {
        MusicBrainzRelease release = new(
            CompilationGroup,
            "Greatest Hits",
            status,
            primaryType,
            secondaryType is null ? [] : [secondaryType],
            "1981");

        Select(Track(), Recording(releases: release)).Should().BeEmpty();
    }

    [Fact]
    public void SelectReleaseGroups_WhenTheAlbumMatchesACompilation_UsesIt()
    {
        MusicBrainzRelease compilation = new(CompilationGroup, "Greatest Hits", "Official", "Album", ["Compilation"], "1981");

        IReadOnlyList<Guid> groups = Select(
            Track(album: "Greatest Hits"),
            Recording(releases: [Album(AlbumGroup, "Jazz"), compilation]));

        groups.Should().Equal(CompilationGroup, AlbumGroup);
    }

    [Fact]
    public void SelectReleaseGroups_PrefersAnExactAlbumMatchOverAContainedOne()
    {
        IReadOnlyList<Guid> groups = Select(
            Track(album: "Greatest Hits"),
            Recording(releases: [Album(OtherGroup, "Greatest Hits II"), Album(AlbumGroup, "Greatest Hits")]));

        groups.Should().Equal(AlbumGroup, OtherGroup);
    }

    [Fact]
    public void SelectReleaseGroups_WhenTheAlbumMatchesNothing_FallsBackToStudioReleases()
    {
        IReadOnlyList<Guid> groups = Select(
            Track(album: "My Mixtape"),
            Recording(releases: Album(AlbumGroup, "Jazz")));

        groups.Should().Equal(AlbumGroup);
    }

    [Fact]
    public void SelectReleaseGroups_WithoutAnAlbum_PrefersAlbumThenEpThenSingle()
    {
        MusicBrainzRelease single = new(SingleGroup, "Don't Stop Me Now", "Official", "Single", [], "1979");
        MusicBrainzRelease ep = new(OtherGroup, "Five Live", "Official", "EP", [], "1978");

        IReadOnlyList<Guid> groups = Select(
            Track(),
            Recording(releases: [single, ep, Album(AlbumGroup, "Jazz")]));

        groups.Should().Equal(AlbumGroup, OtherGroup, SingleGroup);
    }

    [Fact]
    public void SelectReleaseGroups_PrefersTheRecordingWithTheClosestLength()
    {
        MusicBrainzRecording farther = Recording(
            length: TrackLength + TimeSpan.FromSeconds(4),
            releases: Album(OtherGroup, "Jazz (2011 Remaster)"));
        MusicBrainzRecording closer = Recording(
            length: TrackLength + TimeSpan.FromSeconds(1),
            releases: Album(AlbumGroup, "Jazz"));

        Select(Track(), farther, closer).Should().Equal(AlbumGroup, OtherGroup);
    }

    [Fact]
    public void SelectReleaseGroups_PrefersTheEarliestReleaseAndPutsUndatedOnesLast()
    {
        MusicBrainzRelease undated = Album(OtherGroup, "Jazz") with { Date = null };
        MusicBrainzRelease reissue = Album(SingleGroup, "Jazz") with { Date = "2011-06-27" };
        MusicBrainzRelease original = Album(AlbumGroup, "Jazz") with { Date = "1978-11-10" };

        IReadOnlyList<Guid> groups = Select(Track(), Recording(releases: [undated, reissue, original]));

        groups.Should().Equal(AlbumGroup, SingleGroup, OtherGroup);
    }

    [Fact]
    public void SelectReleaseGroups_ListsEachReleaseGroupOnceAndStopsAtTheMaximum()
    {
        MusicBrainzRelease[] releases =
        [
            Album(AlbumGroup, "Jazz") with { Date = "1978" },
            Album(AlbumGroup, "Jazz") with { Date = "1994" },
            Album(SingleGroup, "Jazz") with { Date = "1995" },
            Album(OtherGroup, "Jazz") with { Date = "1996" },
            Album(CompilationGroup, "Jazz") with { Date = "1997" }
        ];

        IReadOnlyList<Guid> groups = Select(Track(), Recording(releases: releases));

        groups.Should().Equal(AlbumGroup, SingleGroup, OtherGroup);
    }

    private static IReadOnlyList<Guid> Select(TrackInfo track, params MusicBrainzRecording[] recordings) =>
        MusicBrainzReleaseMatcher.SelectReleaseGroups(recordings, track, MAX_GROUPS);

    private static TrackInfo Track(string title = "Don't Stop Me Now", string artist = "Queen", string? album = null) =>
        new()
        {
            Id = "local:0123456789abcdef0123456789abcdef",
            Title = title,
            Artist = artist,
            Album = album,
            Duration = TrackLength
        };

    private static MusicBrainzRecording Recording(
        string title = "Don't Stop Me Now",
        string artist = "Queen",
        TimeSpan? length = null,
        params MusicBrainzRelease[] releases) =>
        new(title, [artist], length ?? TrackLength, releases);

    private static MusicBrainzRelease Album(Guid releaseGroupId, string title) =>
        new(releaseGroupId, title, "Official", "Album", [], "1978");
}
