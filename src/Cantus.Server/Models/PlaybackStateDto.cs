namespace Cantus.Server.Models;

public sealed record PlaybackStateDto
{
    public TrackInfoDto? CurrentTrack { get; init; }
    public long ProgressMs { get; init; }
    public bool IsPlaying { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public string? DeviceName { get; init; }
    public int? VolumePercent { get; init; }
    public string? ActiveUserId { get; init; }
    public string? ActiveUserDisplayName { get; init; }
}
