namespace Cantus.Server.BackgroundServices;

public sealed class PlaybackPollerOptions
{
    public const string SECTION_NAME = "PlaybackPoller";

    public int ActivePollIntervalMs { get; set; } = 4000;
    public int ApproachingEndPollIntervalMs { get; set; } = 2500;
    public int ImminentEndPollIntervalMs { get; set; } = 1200;
    public int ApproachingEndThresholdMs { get; set; } = 15000;
    public int ImminentEndThresholdMs { get; set; } = 5000;

    public int PausedPollIntervalMs { get; set; } = 5000;
    public int PausedExtendedPollIntervalMs { get; set; } = 15000;
    public int PausedDeepPollIntervalMs { get; set; } = 30000;

    public int IdlePollIntervalMs { get; set; } = 10000;
    public int IdleExtendedPollIntervalMs { get; set; } = 30000;
    public int IdleDeepPollIntervalMs { get; set; } = 60000;

    public int BackgroundPollIntervalMs { get; set; } = 20000;
    public int DiagnosticsBroadcastIntervalMs { get; set; } = 5000;
}
