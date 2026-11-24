using System;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Whether a track is worth publishing for lyrics: it has a title and an artist, and lasts from
/// 30 seconds to 20 minutes, which leaves out notification sounds, short clips and long videos.
/// </summary>
public static class PublishWorthiness
{
    public const long MIN_DURATION_MS = 30_000;
    public const long MAX_DURATION_MS = 1_200_000;

    public static bool IsWorthPublishing(LocalTrack? track)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
        {
            return false;
        }

        long durationMs = (long)track.Duration.TotalMilliseconds;
        return durationMs is >= MIN_DURATION_MS and <= MAX_DURATION_MS;
    }
}
