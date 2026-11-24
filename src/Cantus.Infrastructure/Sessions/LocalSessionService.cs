using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Infrastructure.Displays;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Sessions;

public sealed class LocalSessionService : ILocalSessionService
{
    private readonly CantusDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<LocalSessionOptions> _options;
    private readonly ILogger<LocalSessionService> _logger;

    public LocalSessionService(
        CantusDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<LocalSessionOptions> options,
        ILogger<LocalSessionService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<LocalSessionCreateResult> CreateAsync(
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (!DisplayService.TryNormalizeName(displayName, out string normalizedName))
        {
            return new LocalSessionCreateResult(LocalSessionCreateStatus.InvalidDisplayName, null, LocalSessionPruneResult.None);
        }

        LocalSessionPruneResult pruned = await PruneIdleAsync(cancellationToken);

        int existing = await _dbContext.UserSessions
            .CountAsync(u => u.Kind == SessionKind.Local, cancellationToken);
        if (existing >= _options.Value.MaxSessions)
        {
            _logger.LogWarning(
                "Refused a Local Session: {Count} exist and the limit is {Limit}",
                existing,
                _options.Value.MaxSessions);
            return new LocalSessionCreateResult(LocalSessionCreateStatus.LimitReached, null, pruned);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        UserSessionEntity entity = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = SessionKind.Local,
            DisplayName = normalizedName,
            ExpiresAtUtc = DateTimeOffset.MaxValue,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _dbContext.UserSessions.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created Local Session {SessionRef}", SessionLogRef.For(entity.Id));
        return new LocalSessionCreateResult(
            LocalSessionCreateStatus.Created,
            SpotifyAuthService.ToModel(entity, accessToken: null, refreshToken: null),
            pruned);
    }

    public async Task TouchAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await _dbContext.UserSessions
            .Where(u => u.Id == sessionId && u.Kind == SessionKind.Local)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.UpdatedAtUtc, now), cancellationToken);
    }

    public async Task<LocalSessionPruneResult> PruneIdleAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-_options.Value.IdleExpiryDays);

        // SQLite cannot compare DateTimeOffset values in a query, so the age check runs here.
        var localSessions = await _dbContext.UserSessions
            .Where(u => u.Kind == SessionKind.Local)
            .Select(u => new { u.Id, u.UpdatedAtUtc })
            .ToListAsync(cancellationToken);

        List<string> idleIds = localSessions
            .Where(s => s.UpdatedAtUtc < cutoff)
            .Select(s => s.Id)
            .ToList();
        if (idleIds.Count == 0)
        {
            return LocalSessionPruneResult.None;
        }

        List<string> displayIds = await _dbContext.Displays
            .Where(d => idleIds.Contains(d.OwnerSessionId))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        // The database cascade deletes each Session's Displays with it.
        int deleted = await _dbContext.UserSessions
            .Where(u => idleIds.Contains(u.Id))
            .ExecuteDeleteAsync(cancellationToken);

        foreach (string id in idleIds)
        {
            SpotifyAuthService.EvictCachedSession(id);
        }

        _logger.LogInformation("Pruned {Count} idle Local Sessions", deleted);
        return new LocalSessionPruneResult(idleIds, displayIds);
    }
}
