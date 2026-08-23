namespace Cantus.Core.Models;

public sealed record TrackInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtUrl { get; init; }
    public TimeSpan Duration { get; init; }
    public bool IsExplicit { get; init; }
}
