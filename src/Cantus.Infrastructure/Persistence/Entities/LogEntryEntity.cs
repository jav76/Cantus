using System;

namespace Cantus.Infrastructure.Persistence.Entities;

public class LogEntryEntity
{
    public long Id { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Logger { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }

    public string? TraceId { get; set; }
}
