using System;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Turns audio into the meter's frequency bands. Capture threads push mono PCM
/// with <see cref="Push"/>; the UI thread calls <see cref="TryAnalyze"/>, which
/// runs a Hann-windowed FFT over the newest samples and folds the bins into
/// log-spaced bands scaled 0..1 over a decibel range. Sources that already
/// have per-bin levels (the browser's AnalyserNode) use <see cref="MapBins"/>
/// directly.
/// </summary>
internal sealed class SpectrumAnalyzer
{
    public const int DEFAULT_FFT_SIZE = 2048;
    public const double MIN_FREQUENCY_HZ = 40;
    public const double MAX_FREQUENCY_HZ = 16000;
    public const double FLOOR_DB = -70;
    public const double CEILING_DB = -12;

    private readonly object _gate = new();
    private readonly float[] _ring;
    private readonly float[] _window;
    private readonly double[] _real;
    private readonly double[] _imag;
    private readonly float[] _binLevels;
    private int _writeIndex;
    private long _samplesSinceAnalysis;

    public SpectrumAnalyzer(int sampleRate, int fftSize = DEFAULT_FFT_SIZE)
    {
        if (fftSize < 16 || (fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fftSize), fftSize, "FFT size must be a power of two of at least 16.");
        }

        SampleRate = sampleRate;
        FftSize = fftSize;
        _ring = new float[fftSize];
        _window = new float[fftSize];
        _real = new double[fftSize];
        _imag = new double[fftSize];
        _binLevels = new float[fftSize / 2];
        for (int i = 0; i < fftSize; i++)
        {
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (fftSize - 1)));
        }
    }

    public int SampleRate { get; }

    public int FftSize { get; }

    /// <summary>Appends mono samples in the range -1..1. Safe to call from any thread.</summary>
    public void Push(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            foreach (float sample in samples)
            {
                _ring[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % _ring.Length;
            }

            _samplesSinceAnalysis += samples.Length;
        }
    }

    /// <summary>
    /// Fills <paramref name="bands"/> from the newest window. Returns false
    /// when no samples arrived since the last call, so a stalled stream reads
    /// as "no data" rather than a frozen spectrum.
    /// </summary>
    public bool TryAnalyze(Span<float> bands)
    {
        lock (_gate)
        {
            if (_samplesSinceAnalysis == 0)
            {
                return false;
            }

            _samplesSinceAnalysis = 0;
            for (int i = 0; i < FftSize; i++)
            {
                int index = (_writeIndex + i) % FftSize;
                _real[i] = _ring[index] * _window[i];
                _imag[i] = 0;
            }
        }

        TransformInPlace(_real, _imag);

        // A full-scale sine through a Hann window peaks at N/4 in magnitude.
        double reference = FftSize / 4.0;
        for (int bin = 0; bin < _binLevels.Length; bin++)
        {
            double magnitude = Math.Sqrt(_real[bin] * _real[bin] + _imag[bin] * _imag[bin]) / reference;
            _binLevels[bin] = (float)DecibelsToLevel(20 * Math.Log10(magnitude + 1e-12));
        }

        MapBins(_binLevels, SampleRate, bands);
        return true;
    }

    /// <summary>Scales a decibel value onto 0..1 between <see cref="FLOOR_DB"/> and <see cref="CEILING_DB"/>.</summary>
    public static double DecibelsToLevel(double decibels) =>
        Math.Clamp((decibels - FLOOR_DB) / (CEILING_DB - FLOOR_DB), 0, 1);

    /// <summary>
    /// Folds per-bin levels (0..1, bin i at i * sampleRate / (2 * binCount) Hz)
    /// into log-spaced bands between <see cref="MIN_FREQUENCY_HZ"/> and
    /// <see cref="MAX_FREQUENCY_HZ"/>. Each band takes its loudest bin, and a
    /// band narrower than one bin reads the bin it falls in.
    /// </summary>
    public static void MapBins(ReadOnlySpan<float> binLevels, int sampleRate, Span<float> bands)
    {
        if (bands.Length == 0)
        {
            return;
        }

        if (binLevels.Length == 0 || sampleRate <= 0)
        {
            bands.Clear();
            return;
        }

        double binHz = sampleRate / 2.0 / binLevels.Length;
        double maxHz = Math.Min(MAX_FREQUENCY_HZ, sampleRate / 2.0);
        double ratio = Math.Log(maxHz / MIN_FREQUENCY_HZ);

        for (int band = 0; band < bands.Length; band++)
        {
            double lowHz = MIN_FREQUENCY_HZ * Math.Exp(ratio * band / bands.Length);
            double highHz = MIN_FREQUENCY_HZ * Math.Exp(ratio * (band + 1) / bands.Length);
            int first = Math.Clamp((int)Math.Floor(lowHz / binHz), 0, binLevels.Length - 1);
            int last = Math.Clamp((int)Math.Ceiling(highHz / binHz) - 1, first, binLevels.Length - 1);

            float peak = 0;
            for (int bin = first; bin <= last; bin++)
            {
                peak = Math.Max(peak, binLevels[bin]);
            }

            bands[band] = peak;
        }
    }

    /// <summary>Iterative radix-2 Cooley-Tukey FFT.</summary>
    internal static void TransformInPlace(double[] real, double[] imag)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            double stepReal = Math.Cos(angle);
            double stepImag = Math.Sin(angle);
            for (int start = 0; start < n; start += length)
            {
                double wReal = 1;
                double wImag = 0;
                int half = length / 2;
                for (int k = 0; k < half; k++)
                {
                    int even = start + k;
                    int odd = even + half;
                    double oddReal = real[odd] * wReal - imag[odd] * wImag;
                    double oddImag = real[odd] * wImag + imag[odd] * wReal;
                    real[odd] = real[even] - oddReal;
                    imag[odd] = imag[even] - oddImag;
                    real[even] += oddReal;
                    imag[even] += oddImag;

                    double nextReal = wReal * stepReal - wImag * stepImag;
                    wImag = wReal * stepImag + wImag * stepReal;
                    wReal = nextReal;
                }
            }
        }
    }
}
