namespace Cantus.Server.BackgroundServices;

public sealed class PlaybackPollerOptions
{
    public const string SectionName = "PlaybackPoller";

    public int ActivePollIntervalMs { get; set; } = 1500;
    public int PausedPollIntervalMs { get; set; } = 5000;
    public int IdlePollIntervalMs { get; set; } = 10000;
    public int DiagnosticsBroadcastIntervalMs { get; set; } = 5000;
}
