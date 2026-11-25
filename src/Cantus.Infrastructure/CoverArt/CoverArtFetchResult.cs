namespace Cantus.Infrastructure.CoverArt;

/// <summary>
/// A catalogue's answer for one track, separating "answered, no confident match" from "could
/// not be reached". Only the first may enter the negative cache.
/// </summary>
public sealed record CoverArtFetchResult(string? ArtUrl, bool IsDefinitive)
{
    public static CoverArtFetchResult Found(string artUrl) => new(artUrl, true);

    public static CoverArtFetchResult NotFound() => new(null, true);

    public static CoverArtFetchResult Unavailable() => new(null, false);
}
