using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Spotify;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class SongPlaybackServiceTests
{
    private const string USER_ID = "user-1";
    private const string TOKEN = "owner-token";
    private const string TRACK_ID = "4u7EnebtmKWzUH433cf5Qv";
    private const string TRACK_URI = "spotify:track:4u7EnebtmKWzUH433cf5Qv";
    private const string RESOLVED_TRACK_ID = "7tFiyTwD0nx5a1eklYtX2J";
    private const string RESOLVED_TRACK_URI = "spotify:track:7tFiyTwD0nx5a1eklYtX2J";
    private const int RETRY_AFTER_SECONDS = 30;
    private const int SONG_DURATION_MS = 354_000;

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<ISpotifyPlayerClient> _player = new();
    private readonly Mock<ISpotifyCatalogClient> _catalog = new();
    private readonly Mock<IPlaybackSessionRegistry> _registry = new();
    private readonly SpotifyRateLimitGate _gate;
    private readonly SongPlaybackService _service;

    public SongPlaybackServiceTests()
    {
        _gate = new SpotifyRateLimitGate(_timeProvider);
        _catalog
            .Setup(c => c.SearchTracksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TrackInfo>());
        _service = new SongPlaybackService(
            _player.Object,
            new SpotifyTrackResolver(_catalog.Object, NullLogger<SpotifyTrackResolver>.Instance),
            _gate,
            _registry.Object,
            NullLogger<SongPlaybackService>.Instance);
    }

    private void SetupPlay(PlayerCommandResult result)
    {
        _player
            .Setup(p => p.PlayTrackAsync(TOKEN, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static TrackInfo ResolvedTrack()
    {
        return new TrackInfo
        {
            Id = RESOLVED_TRACK_ID,
            Title = "Bohemian Rhapsody - Remastered 2011",
            Artist = "Queen",
            Duration = TimeSpan.FromMilliseconds(SONG_DURATION_MS),
        };
    }

    [Fact]
    public async Task PlayAsync_ById_PlaysTheTrackUriWithoutSearchingAndRequestsAPoll()
    {
        SetupPlay(PlayerCommandResult.Success);

        SongPlayResult result = await _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Play, TRACK_ID));

        result.Status.Should().Be(SongPlayStatus.Success);
        result.Track.Should().BeNull();
        _player.Verify(p => p.PlayTrackAsync(TOKEN, TRACK_URI, It.IsAny<CancellationToken>()), Times.Once);
        _catalog.Verify(
            c => c.SearchTracksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _registry.Verify(r => r.RequestUserActivity(USER_ID), Times.AtLeastOnce);
    }

    [Fact]
    public async Task PlayAsync_QueueById_AddsTheTrackUriToTheQueueWithoutAPoll()
    {
        _player
            .Setup(p => p.AddToQueueAsync(TOKEN, TRACK_URI, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);

        SongPlayResult result = await _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Queue, TRACK_ID));

        result.Status.Should().Be(SongPlayStatus.Success);
        _player.Verify(p => p.PlayTrackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(r => r.RequestUserActivity(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PlayAsync_ExternalGuess_ResolvesOnSpotifyThenPlaysTheResolvedTrack()
    {
        TrackInfo resolved = ResolvedTrack();
        _catalog
            .Setup(c => c.SearchTracksAsync(
                TOKEN,
                "track:\"bohemian rhapsody\" artist:\"queen\"",
                SpotifyTrackResolver.SEARCH_LIMIT,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { resolved });
        SetupPlay(PlayerCommandResult.Success);

        SongPlayResult result = await _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Play, null, "Bohemian Rhapsody", "Queen", SONG_DURATION_MS));

        result.Status.Should().Be(SongPlayStatus.Success);
        result.Track.Should().BeSameAs(resolved);
        _player.Verify(p => p.PlayTrackAsync(TOKEN, RESOLVED_TRACK_URI, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlayAsync_ExternalGuessWithNoAcceptedHit_IsNotOnSpotifyAndPlaysNothing()
    {
        _catalog
            .Setup(c => c.SearchTracksAsync(TOKEN, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new TrackInfo { Id = RESOLVED_TRACK_ID, Title = "Somebody to Love", Artist = "Queen" } });

        SongPlayResult result = await _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Play, null, "Bohemian Rhapsody", "Queen"));

        result.Status.Should().Be(SongPlayStatus.NotOnSpotify);
        result.Track.Should().BeNull();
        _catalog.Verify(
            c => c.SearchTracksAsync(TOKEN, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _player.VerifyNoOtherCalls();
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlayAsync_WhileTheGateIsLimited_ReportsRateLimitedWithoutCallingSpotify()
    {
        _gate.Report(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));

        SongPlayResult byId = await _service.PlayAsync(USER_ID, TOKEN, new SongPlayRequest(SongPlayAction.Play, TRACK_ID));
        SongPlayResult external = await _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Queue, null, "Bohemian Rhapsody", "Queen"));

        byId.Status.Should().Be(SongPlayStatus.RateLimited);
        byId.RetryAfter.Should().Be(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));
        external.Status.Should().Be(SongPlayStatus.RateLimited);
        _player.VerifyNoOtherCalls();
        _catalog.Verify(
            c => c.SearchTracksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlayAsync_WhenTheSearchIsRateLimited_ReportsRateLimitedAndPlaysNothing()
    {
        _catalog
            .Setup(c => c.SearchTracksAsync(TOKEN, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpotifyRateLimitedException(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS)));

        SongPlayResult result = await _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Play, null, "Bohemian Rhapsody", "Queen"));

        result.Status.Should().Be(SongPlayStatus.RateLimited);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));
        _player.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlayAsync_WhenTheSearchFails_ReportsFailed()
    {
        _catalog
            .Setup(c => c.SearchTracksAsync(TOKEN, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        SongPlayResult result = await _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Play, null, "Bohemian Rhapsody", "Queen"));

        result.Status.Should().Be(SongPlayStatus.Failed);
        _player.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PlayerCommandResult.NoActiveDevice, SongPlayStatus.NoActiveDevice)]
    [InlineData(PlayerCommandResult.MissingPermissions, SongPlayStatus.MissingPermissions)]
    [InlineData(PlayerCommandResult.Failed, SongPlayStatus.Failed)]
    public async Task PlayAsync_MapsPlayerCommandResults(PlayerCommandResult commandResult, SongPlayStatus expected)
    {
        SetupPlay(commandResult);

        SongPlayResult result = await _service.PlayAsync(USER_ID, TOKEN, new SongPlayRequest(SongPlayAction.Play, TRACK_ID));

        result.Status.Should().Be(expected);
        _registry.Verify(r => r.RequestUserActivity(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PlayAsync_WhenThePlayCommandHitsA429_ReportsRateLimited()
    {
        _player
            .Setup(p => p.PlayTrackAsync(TOKEN, TRACK_URI, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _gate.Report(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));
                return PlayerCommandResult.Failed;
            });

        SongPlayResult result = await _service.PlayAsync(USER_ID, TOKEN, new SongPlayRequest(SongPlayAction.Play, TRACK_ID));

        result.Status.Should().Be(SongPlayStatus.RateLimited);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));
    }

    [Theory]
    [InlineData("4u7EnebtmKWzUH433cf5Q", null, null)]
    [InlineData("4u7EnebtmKWzUH433cf5Qv\n", null, null)]
    [InlineData("4u7EnebtmKWzUH433cf5Q:", null, null)]
    [InlineData(null, "Bohemian Rhapsody", null)]
    [InlineData(null, null, "Queen")]
    public async Task PlayAsync_WithAnInvalidRequest_ThrowsWithoutCallingSpotify(
        string? trackId,
        string? title,
        string? artist)
    {
        Func<Task> act = () => _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(SongPlayAction.Play, trackId, title, artist));

        await act.Should().ThrowAsync<ArgumentException>();
        _player.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlayAsync_WithATitleOrArtistOverTheCap_ThrowsWithoutCallingSpotify(bool longTitle)
    {
        string tooLong = new('a', LyricMatchConstants.MAX_NAME_CHARACTERS + 1);

        Func<Task> act = () => _service.PlayAsync(
            USER_ID,
            TOKEN,
            new SongPlayRequest(
                SongPlayAction.Play,
                null,
                longTitle ? tooLong : "Bohemian Rhapsody",
                longTitle ? "Queen" : tooLong));

        await act.Should().ThrowAsync<ArgumentException>();
        _player.VerifyNoOtherCalls();
    }
}
