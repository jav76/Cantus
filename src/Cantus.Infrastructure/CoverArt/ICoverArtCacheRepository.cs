namespace Cantus.Infrastructure.CoverArt;

/// <summary>One cached answer. A null <see cref="ArtUrl"/> is a remembered "no cover".</summary>
public sealed record CoverArtCacheEntry(string? ArtUrl);

public interface ICoverArtCacheRepository
{
    /// <summary>The entry stored under a Track key, or null when there is none or it has expired.</summary>
    Task<CoverArtCacheEntry?> GetAsync(string trackId, CancellationToken cancellationToken = default);

    /// <summary>Stores a cover, or a "no cover" answer when <paramref name="artUrl"/> is null.</summary>
    Task SaveAsync(
        string trackId,
        string? artUrl,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);
}
