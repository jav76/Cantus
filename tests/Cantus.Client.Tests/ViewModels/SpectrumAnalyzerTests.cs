using System;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class SpectrumAnalyzerTests
{
    private const int SAMPLE_RATE = 48000;
    private const int BAND_COUNT = 32;

    [Fact]
    public void TryAnalyze_FullScaleSine_PeaksInTheBandHoldingItsFrequency()
    {
        // Arrange
        SpectrumAnalyzer analyzer = new(SAMPLE_RATE);
        analyzer.Push(Sine(1000, amplitude: 1.0, count: analyzer.FftSize));
        float[] bands = new float[BAND_COUNT];

        // Act
        bool analyzed = analyzer.TryAnalyze(bands);

        // Assert
        analyzed.Should().BeTrue();
        int expectedBand = BandFor(1000, BAND_COUNT, SAMPLE_RATE);
        Array.IndexOf(bands, Max(bands)).Should().BeCloseTo(expectedBand, 1);
        bands[expectedBand].Should().BeGreaterThan(0.9f);
        bands[BandFor(100, BAND_COUNT, SAMPLE_RATE)].Should().BeLessThan(0.2f);
    }

    [Fact]
    public void TryAnalyze_QuieterSine_GivesALowerLevel()
    {
        // Arrange
        SpectrumAnalyzer loud = new(SAMPLE_RATE);
        SpectrumAnalyzer quiet = new(SAMPLE_RATE);
        loud.Push(Sine(440, amplitude: 0.5, count: loud.FftSize));
        quiet.Push(Sine(440, amplitude: 0.02, count: quiet.FftSize));
        float[] loudBands = new float[BAND_COUNT];
        float[] quietBands = new float[BAND_COUNT];

        // Act
        loud.TryAnalyze(loudBands);
        quiet.TryAnalyze(quietBands);

        // Assert
        Max(quietBands).Should().BeLessThan(Max(loudBands));
        Max(quietBands).Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryAnalyze_Silence_AllBandsZero()
    {
        // Arrange
        SpectrumAnalyzer analyzer = new(SAMPLE_RATE);
        analyzer.Push(new float[analyzer.FftSize]);
        float[] bands = new float[BAND_COUNT];
        bands.AsSpan().Fill(1);

        // Act
        analyzer.TryAnalyze(bands);

        // Assert
        bands.Should().OnlyContain(level => level == 0);
    }

    [Fact]
    public void TryAnalyze_NoNewSamples_ReturnsFalse()
    {
        // Arrange
        SpectrumAnalyzer analyzer = new(SAMPLE_RATE);
        float[] bands = new float[BAND_COUNT];

        // Act / Assert
        analyzer.TryAnalyze(bands).Should().BeFalse();
        analyzer.Push(Sine(440, 0.5, 256));
        analyzer.TryAnalyze(bands).Should().BeTrue();
        analyzer.TryAnalyze(bands).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(8)]
    public void Constructor_FftSizeNotAPowerOfTwoOfAtLeast16_Throws(int fftSize)
    {
        // Act
        Action act = () => _ = new SpectrumAnalyzer(SAMPLE_RATE, fftSize);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MapBins_SingleLoudBin_LandsInTheMatchingBandOnly()
    {
        // Arrange: 1024 bins over 0..24 kHz, so bin 213 sits near 5 kHz.
        float[] bins = new float[1024];
        bins[213] = 1;
        float[] bands = new float[BAND_COUNT];

        // Act
        SpectrumAnalyzer.MapBins(bins, SAMPLE_RATE, bands);

        // Assert
        int expected = BandFor(213 * (SAMPLE_RATE / 2.0 / 1024), BAND_COUNT, SAMPLE_RATE);
        bands[expected].Should().Be(1);
        Sum(bands).Should().Be(1);
    }

    [Fact]
    public void MapBins_NoBins_ClearsBands()
    {
        // Arrange
        float[] bands = [0.5f, 0.5f];

        // Act
        SpectrumAnalyzer.MapBins([], SAMPLE_RATE, bands);

        // Assert
        bands.Should().OnlyContain(level => level == 0);
    }

    [Theory]
    [InlineData(SpectrumAnalyzer.FLOOR_DB - 10, 0)]
    [InlineData(SpectrumAnalyzer.FLOOR_DB, 0)]
    [InlineData(SpectrumAnalyzer.CEILING_DB, 1)]
    [InlineData(0, 1)]
    public void DecibelsToLevel_ClampsToTheMeterRange(double decibels, double expected)
    {
        SpectrumAnalyzer.DecibelsToLevel(decibels).Should().BeApproximately(expected, 1e-9);
    }

    private static float[] Sine(double frequency, double amplitude, int count)
    {
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / SAMPLE_RATE));
        }

        return samples;
    }

    private static int BandFor(double frequency, int bandCount, int sampleRate)
    {
        double maxHz = Math.Min(SpectrumAnalyzer.MAX_FREQUENCY_HZ, sampleRate / 2.0);
        double ratio = Math.Log(maxHz / SpectrumAnalyzer.MIN_FREQUENCY_HZ);
        return (int)Math.Floor(Math.Log(frequency / SpectrumAnalyzer.MIN_FREQUENCY_HZ) / ratio * bandCount);
    }

    private static float Max(float[] values)
    {
        float max = 0;
        foreach (float value in values)
        {
            max = Math.Max(max, value);
        }

        return max;
    }

    private static float Sum(float[] values)
    {
        float sum = 0;
        foreach (float value in values)
        {
            sum += value;
        }

        return sum;
    }
}
