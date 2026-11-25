using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;

namespace Cantus.Infrastructure.CoverArt;

public sealed class SqliteCoverArtCacheRepository : ICoverArtCacheRepository
{
    private readonly CantusDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public SqliteCoverArtCacheRepository(CantusDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<CoverArtCacheEntry?> GetAsync(string trackId, CancellationToken cancellationToken = default)
    {
        CachedCoverArtEntity? entity = await _dbContext.CachedCoverArt.FindAsync([trackId], cancellationToken);
        if (entity is null || entity.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new CoverArtCacheEntry(entity.ArtUrl);
    }

    public async Task SaveAsync(
        string trackId,
        string? artUrl,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        CachedCoverArtEntity? entity = await _dbContext.CachedCoverArt.FindAsync([trackId], cancellationToken);
        if (entity is null)
        {
            entity = new CachedCoverArtEntity { TrackId = trackId };
            _dbContext.CachedCoverArt.Add(entity);
        }

        entity.ArtUrl = artUrl;
        entity.FetchedAtUtc = now;
        entity.ExpiresAtUtc = now.Add(timeToLive);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
