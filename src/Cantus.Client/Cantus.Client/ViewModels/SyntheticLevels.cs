using System;

namespace Cantus.Client.ViewModels;

/// <summary>
/// The decorative meter pattern used where no real audio is available. It is
/// not derived from the music: an invented tempo (seeded by the track id) drives
/// a kick in the low bands, a snare on the backbeat in the mids and hi-hats on
/// the eighths in the highs, over smooth per-band noise. Everything is a pure
/// function of the track id and playback position, so the bars stop when
/// playback pauses, jump with a seek, and repeat exactly on replay.
/// </summary>
internal static class SyntheticLevels
{
    private const double MIN_BPM = 84;
    private const int BPM_SPREAD = 60;
    private const double NOISE_STEP_MS = 170;

    public static double GetBeatsPerMinute(string? trackId) => MIN_BPM + (StableHash(trackId) % BPM_SPREAD);

    public static void Fill(Span<float> bands, string? trackId, long positionMs)
    {
        if (bands.Length == 0)
        {
            return;
        }

        uint seed = StableHash(trackId);
        double beatMs = 60000.0 / GetBeatsPerMinute(trackId);
        double beats = Math.Max(0, positionMs) / beatMs;
        long beatIndex = (long)Math.Floor(beats);
        double beatPhase = beats - beatIndex;
        double eighthPhase = (beats * 2) - Math.Floor(beats * 2);

        double kick = Math.Exp(-beatPhase * 7);
        double snare = (beatIndex % 2 == 1) ? Math.Exp(-beatPhase * 9) : 0;
        double hat = Math.Exp(-eighthPhase * 14);

        // Louder and quieter passages every eight bars, so a long track does not look like a loop.
        long section = beatIndex / 32;
        double energy = 0.7 + 0.3 * Unit(Mix(seed, 0x5EC7, (uint)section));

        double noiseStep = Math.Max(0, positionMs) / NOISE_STEP_MS;
        long noiseIndex = (long)Math.Floor(noiseStep);
        double noiseT = SmoothStep(noiseStep - noiseIndex);

        int lastBand = Math.Max(1, bands.Length - 1);
        for (int band = 0; band < bands.Length; band++)
        {
            double x = (double)band / lastBand;
            double noiseA = Unit(Mix(seed, (uint)band, (uint)noiseIndex));
            double noiseB = Unit(Mix(seed, (uint)band, (uint)(noiseIndex + 1)));
            double noise = noiseA + (noiseB - noiseA) * noiseT;

            double tilt = 0.62 - 0.3 * x;
            double lowWeight = Math.Max(0, 1 - x * 4);
            double midWeight = Math.Exp(-Math.Pow((x - 0.45) / 0.18, 2));
            double highWeight = Math.Max(0, (x - 0.6) / 0.4);

            double level = tilt * (0.35 + 0.5 * noise)
                + 0.45 * kick * lowWeight
                + 0.3 * snare * midWeight
                + 0.25 * hat * highWeight;

            bands[band] = (float)Math.Clamp(level * energy, 0, 1);
        }
    }

    /// <summary>FNV-1a, because string.GetHashCode changes between runs.</summary>
    internal static uint StableHash(string? value)
    {
        uint hash = 2166136261;
        foreach (char c in value ?? string.Empty)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }

    private static uint Mix(uint seed, uint a, uint b)
    {
        uint h = seed ^ (a * 0x9E3779B1) ^ (b * 0x85EBCA77);
        h ^= h >> 15;
        h *= 0x2C1B3C6D;
        h ^= h >> 12;
        h *= 0x297A2D39;
        h ^= h >> 15;
        return h;
    }

    private static double Unit(uint value) => value / (double)uint.MaxValue;

    private static double SmoothStep(double t) => t * t * (3 - 2 * t);
}
