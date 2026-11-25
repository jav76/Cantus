namespace Cantus.Infrastructure.Persistence.Entities;

public sealed class CachedCoverArtEntity
{
    public const int ART_URL_MAX_LENGTH = 2048;

    public required string TrackId { get; set; }

    /// <summary>Null records that the catalogue had no confident match.</summary>
    public string? ArtUrl { get; set; }

    public DateTimeOffset FetchedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }
}
