using System;
using System.Collections.Generic;
using System.Linq;

namespace Cantus.Client.Services;

public sealed record NtpSample(long RttMs, long OffsetMs, long TimestampMs);

/// <summary>
/// Estimates the server clock offset and round-trip time from NTP-style four-timestamp
/// samples: keeps the last <see cref="MAX_NTP_SAMPLES"/> samples, drops the highest-RTT
/// spike once there are three or more, weights the rest by inverse RTT, and smooths the
/// result with an exponential moving average.
/// </summary>
public sealed class ClockOffsetEstimator
{
    public const int MAX_NTP_SAMPLES = 5;
    public const double ALPHA = 0.35;

    private readonly List<NtpSample> _ntpHistory = new();
    private readonly object _ntpLock = new();

    public long RttMs { get; private set; }
    public long ClockOffsetMs { get; private set; }

    /// <summary>False until the first sample, while <see cref="ClockOffsetMs"/> is still 0 by default.</summary>
    public bool IsClockSynced { get; private set; }

    /// <summary>
    /// Computes one sample from the client send (t1), server receive (t2), server send (t3)
    /// and client receive (t4) times. A negative RTT (clock steps, server time spent exceeding
    /// the client round trip) is clamped to 0.
    /// </summary>
    public static NtpSample ComputeSample(long t1, long t2, long t3, long t4)
    {
        long rawRtt = Math.Max(0, (t4 - t1) - (t3 - t2));
        long rawOffset = ((t2 - t1) + (t3 - t4)) / 2;
        return new NtpSample(rawRtt, rawOffset, t4);
    }

    public void AddSample(long t1, long t2, long t3, long t4)
    {
        NtpSample sample = ComputeSample(t1, t2, t3, t4);
        AddSample(sample.RttMs, sample.OffsetMs, sample.TimestampMs);
    }

    public void AddSample(long rawRtt, long rawOffset, long timestampMs)
    {
        lock (_ntpLock)
        {
            _ntpHistory.Add(new NtpSample(rawRtt, rawOffset, timestampMs));
            if (_ntpHistory.Count > MAX_NTP_SAMPLES)
            {
                _ntpHistory.RemoveAt(0);
            }

            List<NtpSample> samples = _ntpHistory.ToList();
            if (samples.Count == 1)
            {
                RttMs = samples[0].RttMs;
                ClockOffsetMs = samples[0].OffsetMs;
                IsClockSynced = true;
                return;
            }

            List<NtpSample> filtered = samples;
            if (samples.Count >= 3)
            {
                long maxRtt = samples.Max(s => s.RttMs);
                NtpSample worstSample = samples.First(s => s.RttMs == maxRtt);
                filtered = samples.Where(s => !ReferenceEquals(s, worstSample)).ToList();
            }

            // Lower latency samples are trusted more.
            double totalWeight = 0;
            double weightedOffsetSum = 0;
            double totalRttSum = 0;

            foreach (NtpSample s in filtered)
            {
                double weight = 1.0 / Math.Max(1, s.RttMs);
                totalWeight += weight;
                weightedOffsetSum += s.OffsetMs * weight;
                totalRttSum += s.RttMs * weight;
            }

            long computedRtt = (long)(totalRttSum / totalWeight);
            long computedOffset = (long)(weightedOffsetSum / totalWeight);

            RttMs = (long)(ALPHA * computedRtt + (1 - ALPHA) * RttMs);
            ClockOffsetMs = (long)(ALPHA * computedOffset + (1 - ALPHA) * ClockOffsetMs);
            IsClockSynced = true;
        }
    }
}
