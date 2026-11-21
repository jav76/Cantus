using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class SongLookupViewModelTests
{
    private const string FRAGMENT = "is this the real life";
    private const string TRACK_ID = "4u7EnebtmKWzUH433cf5Qv";
    private const int PROVIDER_RETRY_SECONDS = 42;

    [Fact]
    public async Task Search_WhenOnlineRecommended_ShowsLibraryGuessesThenReplacesThemWithTheAutoResult()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        TaskCompletionSource<SongLookupCallResult> online = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("none", onlineRecommended: true, Guess("Local Song", local: true))));
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "auto", It.IsAny<CancellationToken>()))
            .Returns(online.Task);
        SongLookupViewModel lookup = CreateLookup(client.Object);
        List<SongLookupState> states = RecordStates(lookup);

        // Act
        Task search = lookup.SearchAsync();

        // Assert - library guesses stay visible while the online call runs
        lookup.State.Should().Be(SongLookupState.SearchingOnline);
        lookup.Guesses.Select(g => g.Title).Should().Equal("Local Song");
        lookup.SearchingVisibility.Should().Be(Visibility.Visible);

        online.SetResult(Success(Response("accepted", onlineRecommended: false, Guess("Online Song", local: false))));
        await search;

        states.Should().Equal(
            SongLookupState.SearchingLibrary,
            SongLookupState.SearchingOnline,
            SongLookupState.Results);
        lookup.Guesses.Select(g => g.Title).Should().Equal("Online Song");
        lookup.StatusText.Should().Be("Best match");
        client.Verify(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.SearchAsync(FRAGMENT, "auto", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Search_WhenOnlineNotRecommended_SkipsTheSecondCall()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response(
                "accepted",
                onlineRecommended: false,
                providers: new[] { Provider("NetEase", "skipped") },
                guesses: new[] { Guess("Local Song", local: true) })));
        SongLookupViewModel lookup = CreateLookup(client.Object);
        List<SongLookupState> states = RecordStates(lookup);

        // Act
        await lookup.SearchAsync();

        // Assert
        states.Should().Equal(SongLookupState.SearchingLibrary, SongLookupState.Results);
        lookup.Guesses.Should().ContainSingle();
        lookup.SearchOnlineVisibility.Should().Be(Visibility.Visible);
        client.Verify(c => c.SearchAsync(It.IsAny<string>(), "auto", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchOnline_SendsTheOnlineScopeAndKeepsGuessesUntilReplaced()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        TaskCompletionSource<SongLookupCallResult> online = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("accepted", onlineRecommended: false, Guess("Local Song", local: true))));
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "online", It.IsAny<CancellationToken>()))
            .Returns(online.Task);
        SongLookupViewModel lookup = CreateLookup(client.Object);
        await lookup.SearchAsync();

        // Act
        Task search = lookup.SearchOnlineAsync();
        IReadOnlyList<string> whileSearching = lookup.Guesses.Select(g => g.Title).ToList();
        online.SetResult(Success(Response("ambiguous", onlineRecommended: false, Guess("Online Song", local: false))));
        await search;

        // Assert
        whileSearching.Should().Equal("Local Song");
        lookup.State.Should().Be(SongLookupState.Results);
        lookup.StatusText.Should().Be("Several songs could match");
        lookup.Guesses.Select(g => g.Title).Should().Equal("Online Song");
    }

    [Fact]
    public async Task Search_IgnoresAStaleResponseThatArrivesAfterANewerSearch()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        TaskCompletionSource<SongLookupCallResult> first = new();
        CancellationToken firstToken = default;
        client
            .Setup(c => c.SearchAsync("first fragment words", "local", It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, _, token) => firstToken = token)
            .Returns(first.Task);
        client
            .Setup(c => c.SearchAsync("second fragment words", "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("accepted", onlineRecommended: false, Guess("Second", local: true))));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        lookup.Fragment = "first fragment words";
        Task stale = lookup.SearchAsync();
        lookup.Fragment = "second fragment words";
        await lookup.SearchAsync();
        first.SetResult(Success(Response("accepted", onlineRecommended: true, Guess("First", local: true))));
        await stale;

        // Assert
        firstToken.IsCancellationRequested.Should().BeTrue();
        lookup.Guesses.Select(g => g.Title).Should().Equal("Second");
        lookup.State.Should().Be(SongLookupState.Results);
        client.Verify(c => c.SearchAsync(It.IsAny<string>(), "auto", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_DropsTheOnlineResponseAndKeepsTheLibraryGuesses()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        TaskCompletionSource<SongLookupCallResult> online = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("none", onlineRecommended: true, Guess("Local Song", local: true))));
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "auto", It.IsAny<CancellationToken>()))
            .Returns(online.Task);
        SongLookupViewModel lookup = CreateLookup(client.Object);
        Task search = lookup.SearchAsync();

        // Act
        lookup.Cancel();
        online.SetResult(Success(Response("accepted", onlineRecommended: false, Guess("Online Song", local: false))));
        await search;

        // Assert
        lookup.State.Should().Be(SongLookupState.Results);
        lookup.StatusText.Should().Be("Possible matches");
        lookup.Guesses.Select(g => g.Title).Should().Equal("Local Song");
    }

    [Fact]
    public async Task Search_NoneOutcomeWithGuesses_ShowsResultsAsPossibleMatches()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("none", onlineRecommended: false, Guess("Maybe", local: true))));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.State.Should().Be(SongLookupState.Results);
        lookup.StatusText.Should().Be("Possible matches");
    }

    [Fact]
    public async Task Search_NoGuesses_IsEmpty()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("none", onlineRecommended: false)));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.State.Should().Be(SongLookupState.Empty);
        lookup.GuessesVisibility.Should().Be(Visibility.Collapsed);
        lookup.StatusText.Should().StartWith("No match found");
    }

    [Theory]
    [InlineData(SongLookupCallStatus.Forbidden, SongLookupState.Error, "needs a signed-in account")]
    [InlineData(SongLookupCallStatus.Unauthorized, SongLookupState.Error, "Sign in with Spotify")]
    [InlineData(SongLookupCallStatus.Failed, SongLookupState.Error, "Couldn't reach")]
    [InlineData(SongLookupCallStatus.RateLimited, SongLookupState.RateLimited, "Try again in a minute")]
    public async Task Search_HttpFailure_MapsToState(
        SongLookupCallStatus status,
        SongLookupState expectedState,
        string expectedText)
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SongLookupCallResult(status));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.State.Should().Be(expectedState);
        lookup.StatusText.Should().Contain(expectedText);
        lookup.Guesses.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_BadRequest_ShowsTheServerMessage()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SongLookupCallResult(SongLookupCallStatus.BadRequest, null, "Fragment is too short."));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.State.Should().Be(SongLookupState.Error);
        lookup.StatusText.Should().Be("Fragment is too short.");
    }

    [Fact]
    public async Task Search_ProviderRateLimitedWithoutGuesses_ShowsTheRetryAfterHint()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("none", onlineRecommended: true)));
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "auto", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response(
                "none",
                onlineRecommended: false,
                providers: new[] { Provider("NetEase", "rateLimited", PROVIDER_RETRY_SECONDS) })));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.State.Should().Be(SongLookupState.RateLimited);
        lookup.StatusText.Should().Contain($"Try again in {PROVIDER_RETRY_SECONDS} seconds");
    }

    [Fact]
    public async Task Search_LocalAnswerWhileTheOnlineBudgetIsSpent_ShowsPausedWithoutTheSearchOnlineLink()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response(
                "ambiguous",
                onlineRecommended: false,
                providers: new[]
                {
                    Provider("NetEase", "rateLimited", PROVIDER_RETRY_SECONDS),
                    Provider("LRCLIB", "rateLimited", PROVIDER_RETRY_SECONDS),
                },
                guesses: new[] { Guess("Local Song", local: true) })));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.State.Should().Be(SongLookupState.Results);
        lookup.StatusText.Should().Contain("Online search is paused")
            .And.Contain($"Try again in {PROVIDER_RETRY_SECONDS} seconds");
        lookup.SearchOnlineVisibility.Should().Be(Visibility.Collapsed);
        client.Verify(c => c.SearchAsync(FRAGMENT, "auto", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Search_OnlineCallFails_KeepsTheLibraryGuesses()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("ambiguous", onlineRecommended: true, Guess("Local Song", local: true))));
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "auto", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SongLookupCallResult(SongLookupCallStatus.RateLimited));
        SongLookupViewModel lookup = CreateLookup(client.Object);

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.State.Should().Be(SongLookupState.Results);
        lookup.Guesses.Select(g => g.Title).Should().Equal("Local Song");
        lookup.StatusText.Should().Contain("Several songs could match").And.Contain("Try again in a minute");
        lookup.SearchOnlineVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task SearchOnline_RetriedAfterARateLimitedResult_KeepsTheCleanOutcomeLabel()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        TaskCompletionSource<SongLookupCallResult> pending = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response(
                "accepted",
                onlineRecommended: false,
                providers: new[] { Provider("NetEase", "rateLimited", PROVIDER_RETRY_SECONDS) },
                guesses: new[] { Guess("Local Song", local: true) })));
        client
            .SetupSequence(c => c.SearchAsync(FRAGMENT, "online", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SongLookupCallResult(SongLookupCallStatus.Failed))
            .ReturnsAsync(new SongLookupCallResult(SongLookupCallStatus.Failed))
            .Returns(pending.Task);
        SongLookupViewModel lookup = CreateLookup(client.Object);
        await lookup.SearchAsync();

        // Act
        await lookup.SearchOnlineAsync();
        await lookup.SearchOnlineAsync();
        string afterRetries = lookup.StatusText;
        Task cancelled = lookup.SearchOnlineAsync();
        lookup.Cancel();
        pending.SetResult(new SongLookupCallResult(SongLookupCallStatus.Failed));
        await cancelled;

        // Assert
        afterRetries.Should().Be($"Best match. Online search failed: {SongLookupViewModel.SERVER_UNREACHABLE_TEXT}");
        lookup.StatusText.Should().Be("Best match");
    }

    [Fact]
    public async Task Search_OnlineCallFails_OffersTheRetryLinkEvenAfterAnOnlineAnswer()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("none", onlineRecommended: true, Guess("Local Song", local: true))));
        client
            .SetupSequence(c => c.SearchAsync(FRAGMENT, "auto", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response(
                "accepted",
                onlineRecommended: false,
                providers: new[] { Provider("NetEase", "ok") },
                guesses: new[] { Guess("Online Song", local: false) })))
            .ReturnsAsync(new SongLookupCallResult(SongLookupCallStatus.Failed));
        SongLookupViewModel lookup = CreateLookup(client.Object);
        await lookup.SearchAsync();
        Visibility afterOnlineAnswer = lookup.SearchOnlineVisibility;

        // Act
        await lookup.SearchAsync();

        // Assert
        afterOnlineAnswer.Should().Be(Visibility.Collapsed);
        lookup.Guesses.Select(g => g.Title).Should().Equal("Local Song");
        lookup.SearchOnlineVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task Search_WithABlankFragment_DoesNotCallTheServer()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        SongLookupViewModel lookup = new(client.Object) { Fragment = "   " };

        // Act
        await lookup.SearchAsync();

        // Assert
        lookup.CanSearch.Should().BeFalse();
        lookup.State.Should().Be(SongLookupState.Idle);
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reset_ClearsTheFragmentGuessesAndState()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.SearchAsync(FRAGMENT, "local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(Response("accepted", onlineRecommended: false, Guess("Local Song", local: true))));
        SongLookupViewModel lookup = CreateLookup(client.Object);
        await lookup.SearchAsync();

        // Act
        lookup.Reset();

        // Assert
        lookup.Fragment.Should().BeEmpty();
        lookup.Guesses.Should().BeEmpty();
        lookup.State.Should().Be(SongLookupState.Idle);
        lookup.StatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void GuessCard_DescribesWhereTheMatchCameFrom()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();

        // Act
        SongGuessViewModel local = new(Guess("A", local: true, confidence: 0.86), client.Object);
        SongGuessViewModel online = new(Guess("B", local: false, confidence: 0.72), client.Object);
        SongGuessViewModel unverified = new(
            Guess("C", local: false, confidence: 0.3) with { Verified = false },
            client.Object);

        // Assert
        local.MatchText.Should().Be("86% match, in your library");
        online.MatchText.Should().Be("72% match, found online (NetEase)");
        unverified.MatchText.Should().Be("found online (NetEase), not verified");
    }

    [Theory]
    [InlineData("A Night at the Opera", "Artist / A Night at the Opera")]
    [InlineData(null, "Artist")]
    [InlineData("  ", "Artist")]
    public void GuessCard_ArtistAlbumText_OmitsAMissingAlbum(string? album, string expected)
    {
        // Arrange
        Mock<ISongLookupClient> client = new();

        // Act
        SongGuessViewModel guess = new(Guess("A", local: true) with { Album = album }, client.Object);

        // Assert
        guess.ArtistAlbumText.Should().Be(expected);
    }

    [Fact]
    public async Task GuessPlay_LocalGuess_PlaysById()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        SongLookupPlayRequestPayload? sent = null;
        client
            .Setup(c => c.PlayAsync(It.IsAny<SongLookupPlayRequestPayload>(), It.IsAny<CancellationToken>()))
            .Callback<SongLookupPlayRequestPayload, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(PlayResult("success"));
        SongGuessViewModel guess = new(Guess("Local Song", local: true), client.Object);

        // Act
        await guess.PlayAsync();

        // Assert
        sent.Should().Be(new SongLookupPlayRequestPayload { Action = "play", SpotifyTrackId = TRACK_ID });
        guess.StatusText.Should().Be("Playing now.");
        guess.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task GuessQueue_OnlineGuess_QueuesByTitleAndArtistAndNamesTheResolvedTrack()
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        SongLookupPlayRequestPayload? sent = null;
        client
            .Setup(c => c.PlayAsync(It.IsAny<SongLookupPlayRequestPayload>(), It.IsAny<CancellationToken>()))
            .Callback<SongLookupPlayRequestPayload, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new SongPlayCallResult(
                SongLookupCallStatus.Success,
                new SongLookupPlayResponsePayload
                {
                    Status = "success",
                    Track = new TrackInfoPayload { Id = TRACK_ID, Title = "Resolved", Artist = "Band" },
                }));
        SongGuessViewModel guess = new(Guess("Online Song", local: false) with { DurationMs = 354000 }, client.Object);

        // Act
        await guess.QueueAsync();

        // Assert
        sent.Should().Be(new SongLookupPlayRequestPayload
        {
            Action = "queue",
            Title = "Online Song",
            Artist = "Artist",
            DurationMs = 354000,
        });
        guess.StatusText.Should().Be("Queued Resolved by Band.");
    }

    [Theory]
    [InlineData("notOnSpotify", "Couldn't find this song on Spotify.")]
    [InlineData("noActiveDevice", PlaybackTransport.NO_ACTIVE_DEVICE_TEXT)]
    [InlineData("missingPermissions", PlaybackTransport.MISSING_PERMISSIONS_TEXT)]
    [InlineData("failed", PlaybackTransport.SPOTIFY_UNREACHABLE_TEXT)]
    [InlineData("rateLimited", "Spotify is busy. Try again in 42 seconds.")]
    public async Task GuessPlay_SpotifyOutcome_SetsTheCardStatus(string status, string expected)
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.PlayAsync(It.IsAny<SongLookupPlayRequestPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayResult(status, retryAfterSeconds: PROVIDER_RETRY_SECONDS));
        SongGuessViewModel guess = new(Guess("Local Song", local: true), client.Object);

        // Act
        await guess.PlayAsync();

        // Assert
        guess.StatusText.Should().Be(expected);
        guess.StatusVisibility.Should().Be(Visibility.Visible);
    }

    [Theory]
    [InlineData(SongLookupCallStatus.Forbidden, "needs a signed-in account")]
    [InlineData(SongLookupCallStatus.RateLimited, "Try again in a minute")]
    [InlineData(SongLookupCallStatus.Failed, "Couldn't reach the Cantus server")]
    public async Task GuessPlay_HttpFailure_SetsTheCardStatus(SongLookupCallStatus status, string expected)
    {
        // Arrange
        Mock<ISongLookupClient> client = new();
        client
            .Setup(c => c.PlayAsync(It.IsAny<SongLookupPlayRequestPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SongPlayCallResult(status));
        SongGuessViewModel guess = new(Guess("Local Song", local: true), client.Object);

        // Act
        await guess.PlayAsync();

        // Assert
        guess.StatusText.Should().Contain(expected);
    }

    private static SongLookupViewModel CreateLookup(ISongLookupClient client)
    {
        return new SongLookupViewModel(client) { Fragment = FRAGMENT };
    }

    private static List<SongLookupState> RecordStates(SongLookupViewModel lookup)
    {
        List<SongLookupState> states = new();
        lookup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SongLookupViewModel.State))
            {
                states.Add(lookup.State);
            }
        };
        return states;
    }

    private static SongLookupCallResult Success(SongLookupResponsePayload response)
    {
        return new SongLookupCallResult(SongLookupCallStatus.Success, response);
    }

    private static SongPlayCallResult PlayResult(string status, int? retryAfterSeconds = null)
    {
        return new SongPlayCallResult(
            SongLookupCallStatus.Success,
            new SongLookupPlayResponsePayload { Status = status, RetryAfterSeconds = retryAfterSeconds });
    }

    private static SongLookupResponsePayload Response(
        string outcome,
        bool onlineRecommended,
        params SongGuessPayload[] guesses)
    {
        return Response(outcome, onlineRecommended, Array.Empty<SongLookupProviderPayload>(), guesses);
    }

    private static SongLookupResponsePayload Response(
        string outcome,
        bool onlineRecommended,
        SongLookupProviderPayload[] providers,
        params SongGuessPayload[] guesses)
    {
        return new SongLookupResponsePayload
        {
            Outcome = outcome,
            OnlineRecommended = onlineRecommended,
            Guesses = guesses,
            Providers = providers,
        };
    }

    private static SongLookupProviderPayload Provider(string name, string status, int? retryAfterSeconds = null)
    {
        return new SongLookupProviderPayload { Name = name, Status = status, RetryAfterSeconds = retryAfterSeconds };
    }

    private static SongGuessPayload Guess(string title, bool local, double confidence = 0.8)
    {
        return new SongGuessPayload
        {
            Source = local ? "Local" : "NetEase",
            SpotifyTrackId = local ? TRACK_ID : null,
            Title = title,
            Artist = "Artist",
            Confidence = confidence,
            Verified = true,
            MatchedLines = new[] { "a matched line" },
            VersionCount = 1,
        };
    }
}
