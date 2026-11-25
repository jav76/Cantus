using System.Net;
using System.Net.Http.Headers;
using Cantus.Infrastructure.CoverArt;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Lyrics.Lookup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public class ProviderRateGateTests
{
    private const string LRCLIB = LrclibLyricsProvider.PROVIDER_NAME;
    private const string NETEASE = NeteaseLyricsProvider.PROVIDER_NAME;

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    private ProviderRateGate CreateGate(SongLookupOptions? options = null) => new(
        _timeProvider,
        Options.Create(options ?? new SongLookupOptions()),
        NullLogger<ProviderRateGate>.Instance);

    [Fact]
    public void NewGate_AdmitsBothKindsOfTraffic()
    {
        ProviderRateGate gate = CreateGate();

        gate.TryAcquire(LRCLIB, isLookup: false, out _).Should().BeTrue();
        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();
        gate.TryAcquire(NETEASE, isLookup: false, out _).Should().BeTrue();
        gate.TryAcquire(NETEASE, isLookup: true, out _).Should().BeTrue();
    }

    [Fact]
    public void UnknownProvider_Throws()
    {
        ProviderRateGate gate = CreateGate();

        Action act = () => gate.TryAcquire("Unknown", isLookup: false, out _);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(MusicBrainzClient.PROVIDER_NAME)]
    [InlineData(CoverArtArchiveClient.PROVIDER_NAME)]
    public void CoverArtCatalogue_WhenBusyWithRetryAfter_PausesItsRequestsForThatLong(string providerName)
    {
        ProviderRateGate gate = CreateGate();
        RetryConditionHeaderValue retryAfter = new(TimeSpan.FromSeconds(5));

        gate.ReportResponse(providerName, isLookup: false, HttpStatusCode.ServiceUnavailable, retryAfter)
            .Should().BeTrue();

        gate.TryAcquire(providerName, isLookup: false, out TimeSpan pausedFor).Should().BeFalse();
        pausedFor.Should().Be(TimeSpan.FromSeconds(5));
        gate.TryAcquire(LRCLIB, isLookup: false, out _).Should().BeTrue();

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        gate.TryAcquire(providerName, isLookup: false, out _).Should().BeTrue();
    }

    [Fact]
    public void LookupBudget_WhenSpent_RefusesLookupsUntilTheWindowRolls()
    {
        ProviderRateGate gate = CreateGate(new SongLookupOptions { LrclibRequestsPerMinute = 2 });

        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();

        gate.TryAcquire(LRCLIB, isLookup: true, out TimeSpan retryAfter).Should().BeFalse();
        retryAfter.Should().Be(TimeSpan.FromSeconds(50));

        _timeProvider.Advance(TimeSpan.FromSeconds(50));
        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();
    }

    [Fact]
    public void LookupBudget_WhenSpent_NeverRefusesNowPlaying()
    {
        ProviderRateGate gate = CreateGate(new SongLookupOptions { NeteaseRequestsPerMinute = 1 });
        gate.TryAcquire(NETEASE, isLookup: true, out _).Should().BeTrue();
        gate.TryAcquire(NETEASE, isLookup: true, out _).Should().BeFalse();

        for (int i = 0; i < 20; i++)
        {
            gate.TryAcquire(NETEASE, isLookup: false, out _).Should().BeTrue();
        }
    }

    [Fact]
    public void NowPlayingRequests_DoNotSpendTheLookupBudget()
    {
        ProviderRateGate gate = CreateGate(new SongLookupOptions { LrclibRequestsPerMinute = 1 });

        for (int i = 0; i < 5; i++)
        {
            gate.TryAcquire(LRCLIB, isLookup: false, out _);
        }

        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();
    }

    [Fact]
    public void LookupBudgets_AreKeptPerProvider()
    {
        ProviderRateGate gate = CreateGate(new SongLookupOptions
        {
            LrclibRequestsPerMinute = 1,
            NeteaseRequestsPerMinute = 1,
        });

        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();

        gate.TryAcquire(NETEASE, isLookup: true, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lrclib429_OnAnyRequest_BlocksAllLrclibTrafficUntilTheClockAdvances(bool isLookup)
    {
        ProviderRateGate gate = CreateGate();

        bool started = gate.ReportResponse(
            LRCLIB,
            isLookup,
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));

        started.Should().BeTrue();
        gate.TryAcquire(LRCLIB, isLookup: false, out TimeSpan nowPlayingRetry).Should().BeFalse();
        nowPlayingRetry.Should().Be(TimeSpan.FromSeconds(30));
        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeFalse();
        gate.TryAcquire(NETEASE, isLookup: false, out _).Should().BeTrue("a cooldown is per provider");

        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        gate.TryAcquire(LRCLIB, isLookup: false, out _).Should().BeTrue();
        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();
    }

    [Fact]
    public void Netease429_OnNowPlaying_BlocksAllNeteaseTraffic()
    {
        ProviderRateGate gate = CreateGate();

        gate.ReportResponse(NETEASE, isLookup: false, HttpStatusCode.TooManyRequests, null);

        gate.TryAcquire(NETEASE, isLookup: false, out _).Should().BeFalse();
        gate.TryAcquire(NETEASE, isLookup: true, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Netease429Or503_OnLookup_BlocksLookupsOnly(HttpStatusCode statusCode)
    {
        ProviderRateGate gate = CreateGate();

        gate.ReportResponse(NETEASE, isLookup: true, statusCode, null).Should().BeTrue();

        gate.TryAcquire(NETEASE, isLookup: true, out TimeSpan retryAfter).Should().BeFalse();
        retryAfter.Should().Be(TimeSpan.FromSeconds(60));
        gate.TryAcquire(NETEASE, isLookup: false, out _).Should().BeTrue();
    }

    [Fact]
    public void ReportLookupCooldown_BlocksLookupsOnly_WithTheDefaultDuration()
    {
        ProviderRateGate gate = CreateGate();

        TimeSpan applied = gate.ReportLookupCooldown(NETEASE);

        applied.Should().Be(TimeSpan.FromSeconds(60));
        gate.TryAcquire(NETEASE, isLookup: true, out _).Should().BeFalse();
        gate.TryAcquire(NETEASE, isLookup: false, out _).Should().BeTrue();
        gate.GetRetryAfter(NETEASE, isLookup: true).Should().Be(TimeSpan.FromSeconds(60));
        gate.GetRetryAfter(NETEASE, isLookup: false).Should().Be(TimeSpan.Zero);

        _timeProvider.Advance(TimeSpan.FromSeconds(60));
        gate.TryAcquire(NETEASE, isLookup: true, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void NonThrottleStatus_NeverStartsACooldown(HttpStatusCode statusCode)
    {
        ProviderRateGate gate = CreateGate();

        gate.ReportResponse(LRCLIB, isLookup: false, statusCode, null).Should().BeFalse();
        gate.ReportResponse(LRCLIB, isLookup: true, statusCode, null).Should().BeFalse();

        gate.TryAcquire(LRCLIB, isLookup: false, out _).Should().BeTrue();
        gate.TryAcquire(LRCLIB, isLookup: true, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lrclib503_WithoutRetryAfter_IsAnOutageNotACooldown(bool isLookup)
    {
        ProviderRateGate gate = CreateGate();

        gate.ReportResponse(LRCLIB, isLookup, HttpStatusCode.ServiceUnavailable, null).Should().BeFalse();

        gate.TryAcquire(LRCLIB, isLookup: false, out _).Should().BeTrue();
    }

    [Fact]
    public void Lrclib503_WithRetryAfter_BlocksAllTraffic()
    {
        ProviderRateGate gate = CreateGate();

        gate.ReportResponse(
            LRCLIB,
            isLookup: false,
            HttpStatusCode.ServiceUnavailable,
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(5))).Should().BeTrue();

        gate.GetRetryAfter(LRCLIB, isLookup: false).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RetryAfter_AsAnHttpDate_IsMeasuredFromTheClock()
    {
        ProviderRateGate gate = CreateGate();
        DateTimeOffset retryAt = _timeProvider.GetUtcNow().AddSeconds(45);

        gate.ReportResponse(
            LRCLIB,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(retryAt));

        gate.GetRetryAfter(LRCLIB, isLookup: false).Should().Be(TimeSpan.FromSeconds(45));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3600, 300)]
    public void RetryAfter_IsClamped(int headerSeconds, int expectedSeconds)
    {
        ProviderRateGate gate = CreateGate();

        gate.ReportResponse(
            LRCLIB,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(headerSeconds)));

        gate.GetRetryAfter(LRCLIB, isLookup: false).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void RetryAfter_DateInThePast_UsesTheMinimum()
    {
        ProviderRateGate gate = CreateGate();

        gate.ReportResponse(
            LRCLIB,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(_timeProvider.GetUtcNow().AddMinutes(-5)));

        gate.GetRetryAfter(LRCLIB, isLookup: false).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ShorterThrottle_NeverEndsAnActiveCooldownEarly()
    {
        ProviderRateGate gate = CreateGate();
        gate.ReportResponse(
            LRCLIB,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)));

        bool extended = gate.ReportResponse(
            LRCLIB,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)));

        extended.Should().BeFalse();
        gate.GetRetryAfter(LRCLIB, isLookup: false).Should().Be(TimeSpan.FromSeconds(120));
    }
}
