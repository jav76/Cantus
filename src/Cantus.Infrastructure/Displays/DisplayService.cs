using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Displays;

public sealed class DisplayService : IDisplayService
{
    private const int TOKEN_BYTE_LENGTH = 32;
    private const int LAST_SEEN_UPDATE_INTERVAL_SECONDS = 60;

    private readonly CantusDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DisplayService> _logger;

    public DisplayService(CantusDbContext dbContext, TimeProvider timeProvider, ILogger<DisplayService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CreatedDisplay> CreateAsync(
        string ownerSessionId,
        string name,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeName(name);

        bool ownerExists = await _dbContext.UserSessions
            .AnyAsync(s => s.Id == ownerSessionId, cancellationToken);
        if (!ownerExists)
        {
            throw new InvalidOperationException("Cannot create a Display for a Session that does not exist.");
        }

        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TOKEN_BYTE_LENGTH));
        DisplayEntity entity = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerSessionId = ownerSessionId,
            Name = normalizedName,
            TokenHash = HashToken(token),
            CreatedAtUtc = _timeProvider.GetUtcNow()
        };

        await _dbContext.Displays.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Display {DisplayId} created", entity.Id);

        return new CreatedDisplay(ToModel(entity), token);
    }

    public async Task<DisplayInfo?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string tokenHash = HashToken(token.Trim());
        DisplayEntity? entity = await _dbContext.Displays
            .FirstOrDefaultAsync(d => d.TokenHash == tokenHash, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan updateInterval = TimeSpan.FromSeconds(LAST_SEEN_UPDATE_INTERVAL_SECONDS);
        if (entity.LastSeenAtUtc is null || now - entity.LastSeenAtUtc.Value >= updateInterval)
        {
            entity.LastSeenAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToModel(entity);
    }

    public async Task<IReadOnlyList<DisplayInfo>> ListAsync(
        string ownerSessionId,
        CancellationToken cancellationToken = default)
    {
        List<DisplayEntity> entities = await _dbContext.Displays
            .AsNoTracking()
            .Where(d => d.OwnerSessionId == ownerSessionId)
            .ToListAsync(cancellationToken);

        // SQLite cannot order by DateTimeOffset server-side.
        return entities
            .OrderBy(d => d.CreatedAtUtc)
            .Select(ToModel)
            .ToList();
    }

    public async Task<bool> RenameAsync(
        string ownerSessionId,
        string displayId,
        string name,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeName(name);

        DisplayEntity? entity = await FindOwnedAsync(ownerSessionId, displayId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Name = normalizedName;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevokeAsync(
        string ownerSessionId,
        string displayId,
        CancellationToken cancellationToken = default)
    {
        DisplayEntity? entity = await FindOwnedAsync(ownerSessionId, displayId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _dbContext.Displays.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Display {DisplayId} revoked", displayId);
        return true;
    }

    private Task<DisplayEntity?> FindOwnedAsync(
        string ownerSessionId,
        string displayId,
        CancellationToken cancellationToken)
    {
        return _dbContext.Displays
            .FirstOrDefaultAsync(
                d => d.Id == displayId && d.OwnerSessionId == ownerSessionId,
                cancellationToken);
    }

    // A Display token is 256 bits from a CSPRNG, so it cannot be guessed or brute-forced
    // from its hash. A single fast SHA-256 is enough; slow password hashes exist to protect
    // low-entropy secrets and would only add latency to every Display request.
    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string NormalizeName(string name)
    {
        if (TryNormalizeName(name, out string normalized))
        {
            return normalized;
        }

        if (normalized.Length == 0)
        {
            throw new ArgumentException("A Display name is required.", nameof(name));
        }

        throw new ArgumentException(
            $"A Display name must be at most {DisplayInfo.MAX_NAME_LENGTH} characters.",
            nameof(name));
    }

    /// <summary>
    /// Trims a user-chosen name and checks it is 1 to <see cref="DisplayInfo.MAX_NAME_LENGTH"/>
    /// characters. Shared with Local Session names, which follow the same rule.
    /// </summary>
    internal static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = name?.Trim() ?? string.Empty;
        return normalized.Length > 0 && normalized.Length <= DisplayInfo.MAX_NAME_LENGTH;
    }

    private static DisplayInfo ToModel(DisplayEntity entity)
    {
        return new DisplayInfo
        {
            Id = entity.Id,
            OwnerSessionId = entity.OwnerSessionId,
            Name = entity.Name,
            CreatedAtUtc = entity.CreatedAtUtc,
            LastSeenAtUtc = entity.LastSeenAtUtc
        };
    }
}
