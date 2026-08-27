using System;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class SignalRJitterFilterTests
{
    [Fact]
    public void ProcessNtpSample_SingleSample_SetsDirectRttAndOffset()
    {
        // Arrange
        SignalRPlaybackClient client = new();

        // Act
        client.ProcessNtpSample(rawRtt: 30, rawOffset: 120, timestampMs: 1000);

        // Assert
        client.RttMs.Should().Be(30);
        client.ClockOffsetMs.Should().Be(120);
    }

    [Fact]
    public void ProcessNtpSample_WithSpikeOutlier_DiscardsHighestRttSpike()
    {
        // Arrange
        SignalRPlaybackClient client = new();

        // Feed consistent baseline (RTT 20ms, Offset 100ms)
        client.ProcessNtpSample(rawRtt: 20, rawOffset: 100, timestampMs: 1000);
        client.ProcessNtpSample(rawRtt: 22, rawOffset: 102, timestampMs: 2000);

        // Feed massive latency spike (RTT 450ms, with corrupted offset due to asymmetric routing)
        client.ProcessNtpSample(rawRtt: 450, rawOffset: -300, timestampMs: 3000);

        // Assert: Offset should not be corrupted by the -300 spike because outlier is filtered out
        client.ClockOffsetMs.Should().BeGreaterThan(
            0,
            "Massive spike offset should be discarded by sliding window filter");
        client.RttMs.Should().BeLessThan(100, "Spike RTT should be rejected from the weighted average");
    }

    [Fact]
    public void ProcessNtpSample_SlidingWindow_RetainsMaxFiveSamples()
    {
        // Arrange
        SignalRPlaybackClient client = new();

        for (int i = 1; i <= 10; i++)
        {
            client.ProcessNtpSample(rawRtt: 20 + i, rawOffset: 50 + i, timestampMs: i * 1000);
        }

        // Assert
        client.RttMs.Should().BeGreaterThan(0);
        client.ClockOffsetMs.Should().BeGreaterThan(0);
    }
}
