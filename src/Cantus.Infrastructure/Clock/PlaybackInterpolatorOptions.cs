namespace Cantus.Infrastructure.Clock;

public sealed class PlaybackInterpolatorOptions
{
    public const string SectionName = "PlaybackInterpolator";
    public int SeekThresholdMs { get; set; } = 2000;
    public int DriftToleranceMs { get; set; } = 250;
}
