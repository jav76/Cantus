namespace Cantus.Core.Models;

public enum CoverArtLookupStatus
{
    /// <summary>A cover is known for the track.</summary>
    Found,

    /// <summary>The catalogue answered and has no confident match. Cached, so it is not asked again soon.</summary>
    NotFound,

    /// <summary>Nothing is known yet: the cache has no entry, or the catalogue could not be reached.</summary>
    Unresolved
}

/// <summary>What the server knows about the cover of a track that came without one.</summary>
public sealed record CoverArtLookup(CoverArtLookupStatus Status, string? ArtUrl)
{
    public static CoverArtLookup Found(string artUrl) => new(CoverArtLookupStatus.Found, artUrl);

    public static CoverArtLookup NotFound() => new(CoverArtLookupStatus.NotFound, null);

    public static CoverArtLookup Unresolved() => new(CoverArtLookupStatus.Unresolved, null);
}
