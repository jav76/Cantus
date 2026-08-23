namespace Cantus.Server.Models;

public sealed record DiagnosticsDto
{
    public int ConnectedClients { get; init; }
    public int AuthorizedSessions { get; init; }
    public string PollerStatus { get; init; } = "Idle";
    public int ActivePollIntervalMs { get; init; }
    public string? ActiveUserId { get; init; }
    public string? ActiveUserName { get; init; }
    public DateTimeOffset ServerTimeUtc { get; init; }
}
