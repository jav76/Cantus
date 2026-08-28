namespace Cantus.Infrastructure.Clock;

public sealed class PlaybackInterpolatorOptions
{
    public const string SECTION_NAME = "PlaybackInterpolator";
    public int SeekThresholdMs { get; set; } = 2000;
    public int DriftToleranceMs { get; set; } = 250;
}
