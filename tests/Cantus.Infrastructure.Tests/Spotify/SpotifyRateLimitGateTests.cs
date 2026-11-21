using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public class SpotifyRateLimitGateTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void NewGate_IsNotLimited()
    {
        SpotifyRateLimitGate gate = new(_timeProvider);

        gate.IsLimited.Should().BeFalse();
        gate.Remaining.Should().Be(TimeSpan.Zero);
        gate.LimitedUntilUtc.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public void Report_PausesUntilRetryAfterElapses()
    {
        SpotifyRateLimitGate gate = new(_timeProvider);

        TimeSpan applied = gate.Report(TimeSpan.FromSeconds(30));

        applied.Should().Be(TimeSpan.FromSeconds(30));
        gate.IsLimited.Should().BeTrue();
        gate.LimitedUntilUtc.Should().Be(_timeProvider.GetUtcNow().AddSeconds(30));
        gate.Remaining.Should().Be(TimeSpan.FromSeconds(30));

        _timeProvider.Advance(TimeSpan.FromSeconds(29));
        gate.IsLimited.Should().BeTrue();
        gate.Remaining.Should().Be(TimeSpan.FromSeconds(1));

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        gate.IsLimited.Should().BeFalse();
        gate.Remaining.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Report_WithoutAPositiveRetryAfter_UsesSixtySeconds(int retryAfterSeconds)
    {
        SpotifyRateLimitGate gate = new(_timeProvider);

        TimeSpan applied = gate.Report(TimeSpan.FromSeconds(retryAfterSeconds));

        applied.Should().Be(TimeSpan.FromSeconds(60));
        gate.Remaining.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Report_WithShorterRetryAfter_NeverShortensAnActivePause()
    {
        SpotifyRateLimitGate gate = new(_timeProvider);
        gate.Report(TimeSpan.FromSeconds(120));
        DateTimeOffset originalUntil = gate.LimitedUntilUtc;

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        gate.Report(TimeSpan.FromSeconds(5));

        gate.LimitedUntilUtc.Should().Be(originalUntil);
        gate.Remaining.Should().Be(TimeSpan.FromSeconds(110));
    }

    [Fact]
    public void Report_WithLongerRetryAfter_ExtendsThePause()
    {
        SpotifyRateLimitGate gate = new(_timeProvider);
        gate.Report(TimeSpan.FromSeconds(10));

        gate.Report(TimeSpan.FromSeconds(90));

        gate.Remaining.Should().Be(TimeSpan.FromSeconds(90));
    }
}
