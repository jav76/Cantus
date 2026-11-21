using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Lyrics.Lookup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics.Lookup;

public sealed class SongLookupServiceTests : IDisposable
{
    private const string USER_ID = "spotify-user";
    private const string LOCAL_FRAGMENT = "the river runs down to the sea tonight";
    private const string ONLINE_FRAGMENT = "golden bells ring out across the frozen hills";
    private const string OTHER_ONLINE_FRAGMENT = "paper lanterns drifting over quiet harbor water";
    private const string ONLINE_LYRICS = "golden bells ring out across the frozen hills\nand nobody hears them";
    private const string UNRELATED_EXCERPT = "zebra quantum marmalade\nvelvet hurricane trombone";
    private const int PAYLOAD_THROTTLE_CODE = -460;
    private const int RATE_LIMIT_SECONDS = 42;
    private const int DURATION_SECONDS = 214;

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly ILyricsCorpusReader _reader = Substitute.For<ILyricsCorpusReader>();
    private readonly ILyricsFetchProvider _netease = CreateProvider(NeteaseLyricsProvider.PROVIDER_NAME);
    private readonly ILyricsFetchProvider _lrclib = CreateProvider(LrclibLyricsProvider.PROVIDER_NAME);
    private readonly SongLookupOptions _options = new();
    private readonly LyricsSearchIndex _index;
    private readonly ProviderRateGate _rateGate;
    private readonly SongLookupQueryCache _queryCache;
    private readonly SongLookupBudget _budget;

    public SongLookupServiceTests()
    {
        IReadOnlyList<LyricsCorpusEntry> corpus =
        [
            Entry("t-river", "River Song", "the river runs down to the sea tonight\nunder a silver moon we sing"),
            Entry("t-morning", "Morning", "wake up early in the morning light\ncoffee on the table and the radio on"),
            Entry("t-rain", "Rain", "rain keeps falling on the window pane\nwaiting for the sun again"),
        ];
        _reader.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(corpus);

        _index = new LyricsSearchIndex(_timeProvider, NullLogger<LyricsSearchIndex>.Instance);
        _rateGate = new ProviderRateGate(
            _timeProvider,
            Options.Create(_options),
            NullLogger<ProviderRateGate>.Instance);
        _queryCache = new SongLookupQueryCache(_timeProvider);
        _budget = new SongLookupBudget(_timeProvider, Options.Create(_options));

        SetHits(_netease, []);
        SetHits(_lrclib, []);
    }

    public void Dispose()
    {
        _index.Dispose();
    }

    private static ILyricsFetchProvider CreateProvider(string name)
    {
        ILyricsFetchProvider provider = Substitute.For<ILyricsFetchProvider, ILyricsFragmentSearchProvider>();
        provider.ProviderName.Returns(name);
        ((ILyricsFragmentSearchProvider)provider).ProviderName.Returns(name);
        return provider;
    }

    private static ILyricsFragmentSearchProvider Search(ILyricsFetchProvider provider) =>
        (ILyricsFragmentSearchProvider)provider;

    private static LyricsCorpusEntry Entry(string trackId, string title, string lyrics) =>
        new(trackId, title, "Some Artist", "Some Album", lyrics, null);

    private static LyricsFragmentHit Hit(string provider, string title, string text) =>
        new(
            new LyricsCandidate(
                provider,
                title,
                title,
                "Online Artist",
                "Online Album",
                TimeSpan.FromSeconds(DURATION_SECONDS),
                false,
                false,
                []),
            text);

    private static void SetHits(ILyricsFetchProvider provider, IReadOnlyList<LyricsFragmentHit> hits) =>
        Search(provider)
            .SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>())
            .Returns(hits);

    private SongLookupService CreateService(params ILyricsFetchProvider[] providers) =>
        new(
            _reader,
            _index,
            providers.Length > 0 ? providers : [_lrclib, _netease],
            _rateGate,
            _queryCache,
            _budget,
            Options.Create(_options),
            _timeProvider,
            NullLogger<SongLookupService>.Instance);

    private SongLookupService CreateTruncatingService() =>
        new(
            _reader,
            _index,
            [_lrclib, _netease],
            _rateGate,
            _queryCache,
            _budget,
            Options.Create(_options),
            _timeProvider,
            NullLogger<SongLookupService>.Instance)
        {
            LocalScanBudget = TimeSpan.Zero,
        };

    /// <summary>The river song first, then enough filler that the scan reaches its first budget check.</summary>
    private void UseCorpusLargerThanOneScanCheck()
    {
        List<LyricsCorpusEntry> corpus =
        [
            Entry("t-river", "River Song", "the river runs down to the sea tonight\nunder a silver moon we sing"),
        ];
        for (int i = 0; i < LyricsSearchIndex.SCAN_CHECK_INTERVAL; i++)
        {
            corpus.Add(Entry($"t-filler-{i}", $"Filler {i}", "wake up early in the morning light\ncoffee on the table"));
        }

        _reader.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(corpus);
    }

    /// <summary>Makes the provider wait on its token; the returned task completes once it is called.</summary>
    private static TaskCompletionSource HangUntilCancelled(ILyricsFetchProvider provider)
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Search(provider)
            .SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return (IReadOnlyList<LyricsFragmentHit>)[];
            });
        return entered;
    }

    private static SongLookupProviderStatus StatusOf(SongLookupResult result, string provider) =>
        result.Providers.Single(p => p.ProviderName == provider);

    private Task ReceivedSearches(ILyricsFetchProvider provider, int count) =>
        Search(provider).Received(count).SearchByFragmentAsync(
            Arg.Any<LyricsFragmentQuery>(),
            Arg.Any<CancellationToken>());

    [Fact]
    public async Task Lookup_WhenLocalIsAccepted_MakesNoProviderCalls()
    {
        SongLookupResult result = await CreateService().LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Auto, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        result.Guesses[0].SpotifyTrackId.Should().Be("t-river");
        result.Guesses[0].Source.Should().Be(LyricsSearchIndex.LOCAL_SOURCE_NAME);
        result.Guesses[0].MatchedLines.Should().NotBeEmpty();
        result.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.Skipped);
        await ReceivedSearches(_netease, 0);
        await ReceivedSearches(_lrclib, 0);
        _budget.TrackedUserCount.Should().Be(0);
    }

    [Fact]
    public async Task Lookup_WithLocalScope_NeverCallsProvidersAndRecommendsOnline()
    {
        SongLookupResult result = await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Local, USER_ID);

        result.Outcome.Should().NotBe(SongMatchOutcome.Accepted);
        result.OnlineRecommended.Should().BeTrue();
        result.Providers.Select(p => p.ProviderName)
            .Should().Equal(NeteaseLyricsProvider.PROVIDER_NAME, LrclibLyricsProvider.PROVIDER_NAME);
        result.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.Skipped);
        await ReceivedSearches(_netease, 0);
        await ReceivedSearches(_lrclib, 0);
        _budget.GetRetryAfter(USER_ID).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Lookup_WithLocalScopeAndAcceptedMatch_DoesNotRecommendOnline()
    {
        SongLookupResult result = await CreateService().LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Local, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        result.OnlineRecommended.Should().BeFalse();
    }

    [Fact]
    public async Task Lookup_WhenNetEaseIsAccepted_SkipsLrclib()
    {
        SetHits(_netease, [Hit(NeteaseLyricsProvider.PROVIDER_NAME, "Golden Bells", ONLINE_LYRICS)]);

        SongLookupResult result = await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        SongGuess top = result.Guesses[0];
        top.Source.Should().Be(NeteaseLyricsProvider.PROVIDER_NAME);
        top.SpotifyTrackId.Should().BeNull();
        top.Verified.Should().BeTrue();
        top.DurationMs.Should().Be((int)TimeSpan.FromSeconds(DURATION_SECONDS).TotalMilliseconds);
        StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Ok);
        StatusOf(result, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Skipped);
        await ReceivedSearches(_lrclib, 0);
    }

    [Fact]
    public async Task Lookup_WhenNetEaseMisses_AsksLrclibWithTitleGuesses()
    {
        SetHits(_lrclib, [Hit(LrclibLyricsProvider.PROVIDER_NAME, "Golden Bells", ONLINE_LYRICS)]);

        SongLookupResult result = await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        result.Guesses[0].Source.Should().Be(LrclibLyricsProvider.PROVIDER_NAME);
        await Search(_lrclib).Received(1).SearchByFragmentAsync(
            Arg.Is<LyricsFragmentQuery>(q =>
                q.TitleGuesses.Count > 0 && q.TitleGuesses.Count <= LrclibLyricsProvider.MAX_TITLE_GUESSES),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_WithOnlineScope_SearchesNetEaseEvenWhenLocalIsAccepted()
    {
        SongLookupResult result = await CreateService().LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Online, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Ok);
        StatusOf(result, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Skipped);
        await ReceivedSearches(_netease, 1);
        await ReceivedSearches(_lrclib, 0);
    }

    [Fact]
    public async Task Lookup_WhenAProviderThrows_ReportsItUnavailableAndDoesNotCacheIt()
    {
        Search(_netease)
            .SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));
        SongLookupService service = CreateService();

        SongLookupResult first = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);
        SongLookupResult second = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        StatusOf(first, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Unavailable);
        StatusOf(first, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Ok);
        StatusOf(second, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Unavailable);
        await ReceivedSearches(_netease, 2);
        await ReceivedSearches(_lrclib, 1);
    }

    [Fact]
    public async Task Lookup_WithADefinitiveAnswer_ReusesItWithoutSpendingTheBudget()
    {
        _options.OnlineLookupsPerMinute = 1;
        SongLookupService service = CreateService();

        SongLookupResult first = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);
        SongLookupResult second = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        first.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.Ok);
        second.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.Ok);
        await ReceivedSearches(_netease, 1);
        await ReceivedSearches(_lrclib, 1);
        _queryCache.Count.Should().Be(2);
    }

    [Fact]
    public async Task Lookup_WithAnEmptyAnswer_ExpiresItAfterTheShortTtl()
    {
        SongLookupService service = CreateService();
        await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        _timeProvider.Advance(TimeSpan.FromMinutes(SongLookupQueryCache.EMPTY_TTL_MINUTES));
        await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        await ReceivedSearches(_netease, 2);
    }

    [Fact]
    public async Task Lookup_WhenTheBudgetIsSpent_ReportsRateLimitedWithoutCallingProviders()
    {
        _options.OnlineLookupsPerMinute = 1;
        SongLookupService service = CreateService();
        await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        SongLookupResult limited = await service.LookupAsync(OTHER_ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);
        SongLookupResult local = await service.LookupAsync(OTHER_ONLINE_FRAGMENT, SongLookupScope.Local, USER_ID);

        SongLookupProviderStatus netease = StatusOf(limited, NeteaseLyricsProvider.PROVIDER_NAME);
        netease.State.Should().Be(SongLookupProviderState.RateLimited);
        netease.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero);
        StatusOf(limited, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.RateLimited);
        await ReceivedSearches(_netease, 1);
        await ReceivedSearches(_lrclib, 1);
        local.OnlineRecommended.Should().BeFalse();
        local.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.RateLimited);
        StatusOf(local, NeteaseLyricsProvider.PROVIDER_NAME).RetryAfter.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Lookup_WithLocalScopeAndAcceptedMatchWhileTheBudgetIsSpent_ReportsProvidersSkipped()
    {
        _options.OnlineLookupsPerMinute = 1;
        SongLookupService service = CreateService();
        await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        SongLookupResult local = await service.LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Local, USER_ID);

        local.Outcome.Should().Be(SongMatchOutcome.Accepted);
        local.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.Skipped);
    }

    [Fact]
    public async Task Lookup_WithOnlineScopeAndNetEaseAbsent_AsksLrclibEvenWhenLocalIsAccepted()
    {
        SongLookupResult result = await CreateService(_lrclib)
            .LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Online, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Disabled);
        StatusOf(result, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Ok);
        await ReceivedSearches(_lrclib, 1);
    }

    [Fact]
    public async Task Lookup_WhenLrclibAnswersAfterTheDeadline_KeepsItsHitsWithoutCachingThem()
    {
        Search(_lrclib)
            .SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _timeProvider.Advance(TimeSpan.FromSeconds(SongLookupService.EXTERNAL_DEADLINE_SECONDS));
                return (IReadOnlyList<LyricsFragmentHit>)
                [
                    Hit(LrclibLyricsProvider.PROVIDER_NAME, "Golden Bells", ONLINE_LYRICS),
                ];
            });
        SongLookupService service = CreateService();

        SongLookupResult first = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);
        await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        StatusOf(first, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Ok);
        first.Guesses[0].Source.Should().Be(LrclibLyricsProvider.PROVIDER_NAME);
        await ReceivedSearches(_lrclib, 2);
    }

    [Fact]
    public async Task Lookup_WhenNetEaseRejectsThePayload_StartsALookupOnlyCooldown()
    {
        Search(_netease)
            .SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProviderPayloadRejectedException(
                NeteaseLyricsProvider.PROVIDER_NAME,
                PAYLOAD_THROTTLE_CODE));

        SongLookupResult result = await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Unavailable);
        _rateGate.GetRetryAfter(NeteaseLyricsProvider.PROVIDER_NAME, isLookup: true)
            .Should().BeGreaterThan(TimeSpan.Zero);
        _rateGate.GetRetryAfter(NeteaseLyricsProvider.PROVIDER_NAME, isLookup: false).Should().Be(TimeSpan.Zero);
        _queryCache.TryGet(NeteaseLyricsProvider.PROVIDER_NAME, ONLINE_FRAGMENT, out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Lookup_WhileNetEaseIsCoolingDown_ReportsRateLimitedWithoutCallingIt()
    {
        _rateGate.ReportLookupCooldown(NeteaseLyricsProvider.PROVIDER_NAME, TimeSpan.FromSeconds(RATE_LIMIT_SECONDS));

        SongLookupResult result = await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        SongLookupProviderStatus netease = StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME);
        netease.State.Should().Be(SongLookupProviderState.RateLimited);
        netease.RetryAfter.Should().Be(TimeSpan.FromSeconds(RATE_LIMIT_SECONDS));
        await ReceivedSearches(_netease, 0);
    }

    [Fact]
    public async Task Lookup_WhenTheProviderIsRateLimited_ReportsItsRetryAfter()
    {
        Search(_netease)
            .SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProviderRateLimitedException(
                NeteaseLyricsProvider.PROVIDER_NAME,
                TimeSpan.FromSeconds(RATE_LIMIT_SECONDS)));

        SongLookupResult result = await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        SongLookupProviderStatus netease = StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME);
        netease.State.Should().Be(SongLookupProviderState.RateLimited);
        netease.RetryAfter.Should().Be(TimeSpan.FromSeconds(RATE_LIMIT_SECONDS));
    }

    [Fact]
    public async Task Lookup_WithNetEaseAbsentAndTitleGuessOff_ReportsBothDisabled()
    {
        _options.LrclibTitleGuessEnabled = false;

        SongLookupResult result = await CreateService(_lrclib)
            .LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        result.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.Disabled);
        await ReceivedSearches(_lrclib, 0);
        _budget.TrackedUserCount.Should().Be(0);
    }

    [Fact]
    public async Task Lookup_WithExternalSearchOff_NeitherCallsProvidersNorRecommendsOnline()
    {
        _options.ExternalSearchEnabled = false;
        SongLookupService service = CreateService();

        SongLookupResult auto = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);
        SongLookupResult local = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Local, USER_ID);

        auto.Providers.Select(p => p.State).Should().AllBeEquivalentTo(SongLookupProviderState.Disabled);
        local.OnlineRecommended.Should().BeFalse();
        await ReceivedSearches(_netease, 0);
    }

    [Fact]
    public async Task Lookup_WithAnUnconfirmedNetEaseHit_AppendsItUnverified()
    {
        SetHits(_netease, [Hit(NeteaseLyricsProvider.PROVIDER_NAME, "Mystery", UNRELATED_EXCERPT)]);
        _options.LrclibTitleGuessEnabled = false;

        SongLookupResult result = await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        SongGuess last = result.Guesses[^1];
        last.Title.Should().Be("Mystery");
        last.Verified.Should().BeFalse();
        last.Confidence.Should().BeLessThan(LyricMatchConstants.RESULT_MIN_SCORE);
        result.Outcome.Should().NotBe(SongMatchOutcome.Accepted);
    }

    [Fact]
    public async Task Lookup_WhenTheCallerCancels_Propagates()
    {
        using CancellationTokenSource caller = new();
        Search(_netease)
            .SearchByFragmentAsync(Arg.Any<LyricsFragmentQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<LyricsFragmentHit>>>(call =>
            {
                caller.Cancel();
                throw new OperationCanceledException(call.Arg<CancellationToken>());
            });

        Func<Task> act = () => CreateService()
            .LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await ReceivedSearches(_lrclib, 0);
    }

    [Fact]
    public async Task Lookup_WithOnlineScopeWhileNetEaseIsCoolingDown_StillSkipsLrclibForAnAcceptedMatch()
    {
        _rateGate.ReportLookupCooldown(NeteaseLyricsProvider.PROVIDER_NAME, TimeSpan.FromSeconds(RATE_LIMIT_SECONDS));

        SongLookupResult result = await CreateService().LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Online, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.RateLimited);
        StatusOf(result, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Skipped);
        await ReceivedSearches(_netease, 0);
        await ReceivedSearches(_lrclib, 0);
    }

    [Fact]
    public async Task Lookup_WithOneLrclibPermitLeft_SendsOneTitleGuessAndDoesNotCacheTheAnswer()
    {
        _options.LrclibRequestsPerMinute = 1;
        SetHits(_lrclib, [Hit(LrclibLyricsProvider.PROVIDER_NAME, "Golden Bells", ONLINE_LYRICS)]);
        SongLookupService service = CreateService();

        SongLookupResult first = await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);
        await service.LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        StatusOf(first, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Ok);
        await Search(_lrclib).Received(2).SearchByFragmentAsync(
            Arg.Is<LyricsFragmentQuery>(q => q.TitleGuesses.Count == 1),
            Arg.Any<CancellationToken>());
        _queryCache.TryGet(LrclibLyricsProvider.PROVIDER_NAME, ONLINE_FRAGMENT, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Lookup_WithATruncatedScan_DoesNotAcceptTheLocalMatch()
    {
        _options.ExternalSearchEnabled = false;
        UseCorpusLargerThanOneScanCheck();

        SongLookupResult result = await CreateTruncatingService()
            .LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Auto, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Ambiguous);
        result.Guesses[0].SpotifyTrackId.Should().Be("t-river");
    }

    [Fact]
    public async Task Lookup_WithATruncatedScan_StillAcceptsADecisiveNetEaseHit()
    {
        UseCorpusLargerThanOneScanCheck();
        SetHits(_netease, [Hit(NeteaseLyricsProvider.PROVIDER_NAME, "Golden Bells", ONLINE_LYRICS)]);

        SongLookupResult result = await CreateTruncatingService()
            .LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        result.Outcome.Should().Be(SongMatchOutcome.Accepted);
        result.Guesses[0].Source.Should().Be(NeteaseLyricsProvider.PROVIDER_NAME);
        await ReceivedSearches(_lrclib, 0);
    }

    [Fact]
    public async Task Lookup_WhenNetEaseOutlivesTheDeadline_ReportsItUnavailableAndKeepsLocalGuesses()
    {
        TaskCompletionSource entered = HangUntilCancelled(_netease);

        Task<SongLookupResult> lookup = CreateService()
            .LookupAsync(LOCAL_FRAGMENT, SongLookupScope.Online, USER_ID);
        await entered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(SongLookupService.EXTERNAL_DEADLINE_SECONDS));
        SongLookupResult result = await lookup;

        StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Unavailable);
        StatusOf(result, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Skipped);
        result.Guesses[0].SpotifyTrackId.Should().Be("t-river");
        await ReceivedSearches(_lrclib, 0);
    }

    [Fact]
    public async Task Lookup_AfterTheDeadline_ReportsLrclibUnavailableWithoutCallingIt()
    {
        TaskCompletionSource entered = HangUntilCancelled(_netease);

        Task<SongLookupResult> lookup = CreateService()
            .LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);
        await entered.Task;
        _timeProvider.Advance(TimeSpan.FromSeconds(SongLookupService.EXTERNAL_DEADLINE_SECONDS));
        SongLookupResult result = await lookup;

        StatusOf(result, NeteaseLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Unavailable);
        StatusOf(result, LrclibLyricsProvider.PROVIDER_NAME).State.Should().Be(SongLookupProviderState.Unavailable);
        await ReceivedSearches(_lrclib, 0);
    }

    [Fact]
    public async Task Lookup_SendsAtMostTheProviderRequestCapPerLookup()
    {
        await CreateService().LookupAsync(ONLINE_FRAGMENT, SongLookupScope.Auto, USER_ID);

        await ReceivedSearches(_netease, 1);
        await Search(_lrclib).Received(1).SearchByFragmentAsync(
            Arg.Is<LyricsFragmentQuery>(q =>
                q.TitleGuesses.Count <= SongLookupService.MAX_PROVIDER_REQUESTS_PER_LOOKUP - 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_WithATooShortFragment_Throws()
    {
        Func<Task> act = () => CreateService().LookupAsync("hi", SongLookupScope.Auto, USER_ID);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Budget_WhenUsersGoIdle_DropsTheirEntries()
    {
        _budget.TryAcquire(USER_ID, out _).Should().BeTrue();
        _budget.TrackedUserCount.Should().Be(1);

        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        _budget.TrackedUserCount.Should().Be(0);
    }

    [Fact]
    public void QueryCache_WhenFull_EvictsTheOldestEntry()
    {
        for (int i = 0; i <= SongLookupQueryCache.MAX_ENTRIES; i++)
        {
            _queryCache.Store(NeteaseLyricsProvider.PROVIDER_NAME, $"fragment {i}", []);
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        _queryCache.Count.Should().Be(SongLookupQueryCache.MAX_ENTRIES);
        _queryCache.TryGet(NeteaseLyricsProvider.PROVIDER_NAME, "fragment 0", out _).Should().BeFalse();
        _queryCache.TryGet(LrclibLyricsProvider.PROVIDER_NAME, "fragment 1", out _).Should().BeFalse();
        _queryCache.TryGet(NeteaseLyricsProvider.PROVIDER_NAME, "fragment 1", out _).Should().BeTrue();
    }
}
