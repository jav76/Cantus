namespace Cantus.Server.Models;

public sealed record ClockSyncRequest(long ClientSendTimeMs);

public sealed record ClockSyncResponse(
    long ClientSendTimeMs,
    long ServerReceiveTimeMs,
    long ServerSendTimeMs);
