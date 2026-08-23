namespace Cantus.Server.Models;

public sealed record TrackInfoDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtUrl { get; init; }
    public long DurationMs { get; init; }
    public bool IsExplicit { get; init; }
}
