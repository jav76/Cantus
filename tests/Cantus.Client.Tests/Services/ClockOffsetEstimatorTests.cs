using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ClockOffsetEstimatorTests
{
    [Fact]
    public void NoSamples_ReportsZeroRttAndOffset()
    {
        // Arrange & Act
        ClockOffsetEstimator estimator = new();

        // Assert
        estimator.RttMs.Should().Be(0);
        estimator.ClockOffsetMs.Should().Be(0);
        estimator.IsClockSynced.Should().BeFalse();
    }

    [Fact]
    public void IsClockSynced_BecomesTrueWithTheFirstSampleAndStaysTrue()
    {
        // Arrange
        ClockOffsetEstimator estimator = new();

        // Act & Assert
        estimator.AddSample(t1: 1000, t2: 1510, t3: 1512, t4: 1030);
        estimator.IsClockSynced.Should().BeTrue();
        estimator.AddSample(rawRtt: 30, rawOffset: 0, timestampMs: 2000);
        estimator.IsClockSynced.Should().BeTrue();
    }

    [Fact]
    public void ComputeSample_ExcludesServerTimeFromRttAndAveragesBothLegsForOffset()
    {
        // Arrange: the server clock is about 496 ms ahead and spent 2 ms handling the call.
        // Act
        NtpSample sample = ClockOffsetEstimator.ComputeSample(t1: 1000, t2: 1510, t3: 1512, t4: 1030);

        // Assert
        sample.RttMs.Should().Be(28);
        sample.OffsetMs.Should().Be(496);
        sample.TimestampMs.Should().Be(1030);
    }

    [Fact]
    public void ComputeSample_WithServerTimeLongerThanTheRoundTrip_ClampsRttToZero()
    {
        // Act
        NtpSample sample = ClockOffsetEstimator.ComputeSample(t1: 1000, t2: 2000, t3: 2100, t4: 1050);

        // Assert
        sample.RttMs.Should().Be(0);
        sample.OffsetMs.Should().Be(1025);
    }

    [Fact]
    public void ComputeSample_TruncatesAnOddOffsetTowardZero()
    {
        // Act
        NtpSample sample = ClockOffsetEstimator.ComputeSample(t1: 0, t2: 0, t3: 0, t4: 3);

        // Assert
        sample.RttMs.Should().Be(3);
        sample.OffsetMs.Should().Be(-1);
    }

    [Fact]
    public void AddSample_FirstSample_IsTakenDirectly()
    {
        // Arrange
        ClockOffsetEstimator estimator = new();

        // Act
        estimator.AddSample(t1: 1000, t2: 1510, t3: 1512, t4: 1030);

        // Assert
        estimator.RttMs.Should().Be(28);
        estimator.ClockOffsetMs.Should().Be(496);
    }

    [Fact]
    public void AddSample_ZeroRttSamples_AreWeightedWithoutDividingByZero()
    {
        // Arrange
        ClockOffsetEstimator estimator = new();

        // Act: both samples have a negative raw RTT, clamped to 0.
        estimator.AddSample(t1: 1000, t2: 2000, t3: 2100, t4: 1050);
        estimator.AddSample(t1: 2000, t2: 3000, t3: 3100, t4: 2050);

        // Assert
        estimator.RttMs.Should().Be(0);
        estimator.ClockOffsetMs.Should().Be(1025);
    }

    [Fact]
    public void AddSample_TwoSamples_WeightsByInverseRttThenSmooths()
    {
        // Arrange
        ClockOffsetEstimator estimator = new();
        estimator.AddSample(rawRtt: 10, rawOffset: 100, timestampMs: 1000);

        // Act
        estimator.AddSample(rawRtt: 20, rawOffset: 200, timestampMs: 2000);

        // Assert: weighted RTT 13 and offset 133, then 0.35 * new + 0.65 * previous.
        estimator.RttMs.Should().Be(11);
        estimator.ClockOffsetMs.Should().Be(111);
    }

    [Fact]
    public void AddSample_TwoSamples_KeepsAHighRttSampleBecauseFilteringNeedsThree()
    {
        // Arrange
        ClockOffsetEstimator estimator = new();
        estimator.AddSample(rawRtt: 20, rawOffset: 100, timestampMs: 1000);

        // Act
        estimator.AddSample(rawRtt: 400, rawOffset: -1000, timestampMs: 2000);

        // Assert
        estimator.RttMs.Should().Be(26);
        estimator.ClockOffsetMs.Should().Be(81);
    }

    [Fact]
    public void AddSample_ThreeSamples_DiscardsTheHighestRttSpike()
    {
        // Arrange
        ClockOffsetEstimator estimator = new();
        estimator.AddSample(rawRtt: 20, rawOffset: 100, timestampMs: 1000);
        estimator.AddSample(rawRtt: 20, rawOffset: 100, timestampMs: 2000);

        // Act
        estimator.AddSample(rawRtt: 400, rawOffset: -1000, timestampMs: 3000);

        // Assert
        estimator.RttMs.Should().Be(20);
        estimator.ClockOffsetMs.Should().Be(100);
    }

    [Fact]
    public void AddSample_TiedSpikes_DiscardsOnlyOne()
    {
        // Arrange
        ClockOffsetEstimator estimator = new();
        estimator.AddSample(rawRtt: 20, rawOffset: 100, timestampMs: 1000);
        estimator.AddSample(rawRtt: 400, rawOffset: -1000, timestampMs: 2000);

        // Act
        estimator.AddSample(rawRtt: 400, rawOffset: -1000, timestampMs: 3000);

        // Assert: the remaining spike still pulls the estimate (38 ms, 47 ms before smoothing).
        estimator.RttMs.Should().Be(30);
        estimator.ClockOffsetMs.Should().Be(69);
    }

    [Fact]
    public void AddSample_BeyondTheWindow_EvictsTheOldestSample()
    {
        // Arrange: a lowest-RTT sample is never discarded as a spike, so only eviction removes it.
        ClockOffsetEstimator estimator = new();
        estimator.AddSample(rawRtt: 1, rawOffset: 1000, timestampMs: 0);

        // Act
        for (int i = 1; i <= 30; i++)
        {
            estimator.AddSample(rawRtt: 100, rawOffset: 0, timestampMs: i * 1000);
        }

        // Assert
        estimator.ClockOffsetMs.Should().Be(0);
        estimator.RttMs.Should().BeInRange(90, 100);
    }
}
