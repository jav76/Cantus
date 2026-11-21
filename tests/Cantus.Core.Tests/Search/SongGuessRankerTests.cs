using Cantus.Core.Search;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Search;

public class SongGuessRankerTests
{
    private const string SHINE_A = "we shine shine shine tonight\nthe river runs to the open sea\nhold my hand";
    private const string SHINE_B = "we shine shine shine tonight\nengines roar across the city\ncold neon rain";
    private const string DAISY = "daisy daisy give me your answer do\nim half crazy all for the love of you";

    [Fact]
    public void Rank_RemasterUnderTwoTrackIds_GroupsAndAccepts()
    {
        SongCandidate original = Local("track-a", "Daisy Bell", "Dan Dacre", 0.95, DAISY);
        SongCandidate remaster = Local("track-b", "Daisy Bell - 2011 Remaster", "Dan Dacre", 0.95, DAISY);
        SongCandidate other = Local("track-c", "Other Song", "Someone", 0.55, SHINE_A);

        SongGuessRanking ranking = SongGuessRanker.Rank([remaster, other, original]);

        ranking.Outcome.Should().Be(SongMatchOutcome.Accepted);
        ranking.Guesses.Should().HaveCount(2);
        ranking.Guesses[0].VersionCount.Should().Be(2);
        ranking.Guesses[0].Head.SpotifyTrackId.Should().Be("track-a");
        ranking.Margin.Should().BeApproximately(0.40, 0.0001);
    }

    [Fact]
    public void Rank_SameTitleDifferentSongsSharingTheLine_IsAmbiguous()
    {
        SongCandidate first = Local("track-a", "Shine", "The Hollow Pines", 0.97, SHINE_A);
        SongCandidate second = Local("track-b", "Shine", "Mara Quell", 0.97, SHINE_B);

        SongGuessRanking ranking = SongGuessRanker.Rank([first, second]);

        ranking.Outcome.Should().Be(SongMatchOutcome.Ambiguous);
        ranking.Guesses.Should().HaveCount(2);
        ranking.Guesses.Should().OnlyContain(g => g.VersionCount == 1);
    }

    [Fact]
    public void Rank_CoverWithTheSameLyrics_GroupsByLyricEvidence()
    {
        SongCandidate first = Local("track-a", "Daisy Bell", "Dan Dacre", 0.95, DAISY);
        SongCandidate cover = Local("track-b", "Daisy Bell", "A Cover Band", 0.94, DAISY);

        SongGuessRanking ranking = SongGuessRanker.Rank([first, cover]);

        ranking.Outcome.Should().Be(SongMatchOutcome.Accepted);
        ranking.Guesses.Should().ContainSingle().Which.VersionCount.Should().Be(2);
    }

    [Theory]
    [InlineData(0.90, 0.85, SongMatchOutcome.Ambiguous)]
    [InlineData(0.90, 0.80, SongMatchOutcome.Accepted)]
    [InlineData(0.69, 0.10, SongMatchOutcome.Ambiguous)]
    [InlineData(0.45, 0.00, SongMatchOutcome.None)]
    public void Rank_AppliesTheDecisionBandsToDifferentSongs(double top, double second, SongMatchOutcome expected)
    {
        SongCandidate first = Local("track-a", "First Song", "Artist A", top, DAISY);
        SongCandidate runnerUp = Local("track-b", "Second Song", "Artist B", second, SHINE_A);

        SongGuessRanker.Rank([first, runnerUp]).Outcome.Should().Be(expected);
    }

    [Fact]
    public void Rank_ExternalExcerptOfTheSameWork_GroupsWithTheLocalHeadAndLocalWinsTies()
    {
        SongCandidate external = External("NetEase", "Daisy Bell", "Dacre, Dan", 0.95, "give me your answer do im half crazy");
        SongCandidate local = Local("track-a", "Daisy Bell", "Dan Dacre", 0.95, DAISY);

        SongGuessRanking ranking = SongGuessRanker.Rank([external, local]);

        ranking.Guesses.Should().ContainSingle();
        ranking.Guesses[0].Head.Origin.Should().Be(SongCandidateOrigin.Local);
        ranking.Guesses[0].VersionCount.Should().Be(2);
    }

    [Fact]
    public void Rank_ExternalExcerptThatDoesNotAlign_StaysSeparate()
    {
        SongCandidate local = Local("track-a", "Shine", "The Hollow Pines", 0.95, SHINE_A);
        SongCandidate external = External("NetEase", "Shine", "Mara Quell", 0.93, "engines roar across the city cold neon rain");

        SongGuessRanking ranking = SongGuessRanker.Rank([local, external]);

        ranking.Guesses.Should().HaveCount(2);
        ranking.Outcome.Should().Be(SongMatchOutcome.Ambiguous);
    }

    [Fact]
    public void Rank_UnverifiedHits_AreExcludedFromGroupingAndTheOutcomeAndAppendedLast()
    {
        SongCandidate local = Local("track-a", "Daisy Bell", "Dan Dacre", 0.80, DAISY);
        SongCandidate unverified = External("NetEase", "Daisy Bell", "Dan Dacre", 0.20, DAISY) with { Verified = false };
        SongCandidate unverifiedRival = External("NetEase", "Rival", "Rival", 0.10, SHINE_A) with { Verified = false };

        SongGuessRanking ranking = SongGuessRanker.Rank([unverified, unverifiedRival, local]);

        ranking.Outcome.Should().Be(SongMatchOutcome.Accepted);
        ranking.Guesses.Select(g => g.Head).Should().Equal(local, unverified, unverifiedRival);
        ranking.Guesses[0].VersionCount.Should().Be(1);
    }

    [Fact]
    public void Rank_DropsLowScoresAndCapsTheGuesses()
    {
        List<SongCandidate> candidates = Enumerable
            .Range(0, LyricMatchConstants.MAX_GUESSES + 3)
            .Select(i => Local($"track-{i}", $"Song {i}", $"Artist {i}", 0.9 - i * 0.01, DAISY))
            .Append(Local("track-low", "Low", "Low", LyricMatchConstants.RESULT_MIN_SCORE - 0.01, SHINE_A))
            .ToList();

        SongGuessRanking ranking = SongGuessRanker.Rank(candidates);

        ranking.Guesses.Should().HaveCount(LyricMatchConstants.MAX_GUESSES);
        ranking.Guesses.Should().NotContain(g => g.Head.SpotifyTrackId == "track-low");
    }

    [Fact]
    public void Rank_NoCandidates_IsNone()
    {
        SongGuessRanking ranking = SongGuessRanker.Rank([]);

        ranking.Outcome.Should().Be(SongMatchOutcome.None);
        ranking.Guesses.Should().BeEmpty();
    }

    private static SongCandidate Local(string trackId, string title, string artist, double score, string lyrics) =>
        new(
            SongCandidateOrigin.Local,
            "Local",
            trackId,
            title,
            artist,
            null,
            null,
            score,
            true,
            [],
            LyricTextNormalizer.Normalize(lyrics).Tokens);

    private static SongCandidate External(string provider, string title, string artist, double score, string excerpt) =>
        new(
            SongCandidateOrigin.External,
            provider,
            null,
            title,
            artist,
            null,
            null,
            score,
            true,
            [],
            LyricTextNormalizer.Normalize(excerpt).Tokens);
}
