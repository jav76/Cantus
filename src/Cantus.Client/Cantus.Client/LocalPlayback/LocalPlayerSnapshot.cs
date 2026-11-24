using System;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// One local player at a moment in time. Times are on the monotonic clock of
/// <see cref="MonotonicClock"/>; <see cref="Position"/> was sampled at <see cref="PositionCapturedAt"/>.
/// </summary>
/// <param name="InstanceId">The player's unique address on its source, e.g. its D-Bus name.</param>
/// <param name="AppId">The stable app identity used for the per-player switch.</param>
/// <param name="LastStartedPlayingAt">When the player last entered Playing, or null if never seen playing.</param>
/// <param name="SeekGeneration">Increases each time the player reports a seek.</param>
public sealed record LocalPlayerSnapshot(
    string InstanceId,
    string AppId,
    string AppName,
    LocalPlayerStatus Status,
    LocalTrack? Track,
    TimeSpan Position,
    TimeSpan PositionCapturedAt,
    double Rate,
    LocalPlayerCapabilities Capabilities,
    TimeSpan? LastStartedPlayingAt,
    int SeekGeneration = 0)
{
    /// <summary>The position at <paramref name="now"/>, extrapolated with the rate while playing.</summary>
    public TimeSpan ProjectPosition(TimeSpan now) =>
        Project(Status, Position, PositionCapturedAt, Rate, Track?.Duration ?? TimeSpan.Zero, now);

    public static TimeSpan Project(
        LocalPlayerStatus status,
        TimeSpan position,
        TimeSpan capturedAt,
        double rate,
        TimeSpan duration,
        TimeSpan now)
    {
        TimeSpan projected = position;
        if (status == LocalPlayerStatus.Playing && now > capturedAt && double.IsFinite(rate) && rate > 0)
        {
            projected += TimeSpan.FromTicks((long)((now - capturedAt).Ticks * rate));
        }

        if (projected < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return duration > TimeSpan.Zero && projected > duration ? duration : projected;
    }
}
