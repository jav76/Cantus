using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// Reads the effective lyrics with two no-tracking projections, one over
/// <c>LyricsOverrides</c> and one over positive <c>CachedLyrics</c> rows, merged in memory.
/// Expiry is filtered in C# because EF SQLite cannot compare <see cref="DateTimeOffset"/>
/// values server-side.
/// </summary>
public sealed class SqliteLyricsCorpusReader : ILyricsCorpusReader
{
    private readonly CantusDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public SqliteLyricsCorpusReader(CantusDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<LyricsCorpusEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        List<OverrideRow> overrides = await ProjectOverrides(_dbContext.LyricsOverrides)
            .ToListAsync(cancellationToken);
        List<CacheRow> cacheRows = await ProjectCacheRows(_dbContext.CachedLyrics)
            .ToListAsync(cancellationToken);

        return await MergeAsync(overrides, cacheRows, cancellationToken);
    }

    public async Task<IReadOnlyList<LyricsCorpusEntry>> ReadAsync(
        IReadOnlyCollection<string> trackIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        if (trackIds.Count == 0)
        {
            return [];
        }

        // A List keeps Contains translatable; an array binds to the span overload under C# 14.
        List<string> ids = trackIds.Distinct(StringComparer.Ordinal).ToList();
        List<OverrideRow> overrides = await ProjectOverrides(
                _dbContext.LyricsOverrides.Where(o => ids.Contains(o.TrackId)))
            .ToListAsync(cancellationToken);
        List<CacheRow> cacheRows = await ProjectCacheRows(
                _dbContext.CachedLyrics.Where(c => ids.Contains(c.TrackId)))
            .ToListAsync(cancellationToken);

        return await MergeAsync(overrides, cacheRows, cancellationToken);
    }

    private static IQueryable<OverrideRow> ProjectOverrides(IQueryable<LyricsOverrideEntity> source) =>
        source
            .AsNoTracking()
            .Select(o => new OverrideRow(
                o.TrackId,
                o.Kind,
                o.TrackName,
                o.ArtistName,
                o.AlbumName,
                o.PlainLyrics,
                o.IsInstrumental));

    private static IQueryable<CacheRow> ProjectCacheRows(IQueryable<CachedLyricsEntity> source) =>
        source
            .AsNoTracking()
            .Where(c => !c.IsNotFound && !c.IsInstrumental && c.PlainLyrics != null)
            .Select(c => new CacheRow(
                c.TrackId,
                c.TrackName,
                c.ArtistName,
                c.AlbumName,
                c.PlainLyrics!,
                c.ExpiresAtUtc));

    private async Task<IReadOnlyList<LyricsCorpusEntry>> MergeAsync(
        List<OverrideRow> overrides,
        List<CacheRow> cacheRows,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        Dictionary<string, CacheRow> cacheById = cacheRows.ToDictionary(c => c.TrackId, StringComparer.Ordinal);
        Dictionary<string, NameRow> fallbackNames = await LoadFallbackNamesAsync(
            overrides,
            cacheById,
            cancellationToken);

        List<LyricsCorpusEntry> entries = new(cacheRows.Count + overrides.Count);
        HashSet<string> shadowed = new(StringComparer.Ordinal);
        foreach (OverrideRow row in overrides)
        {
            shadowed.Add(row.TrackId);
            if (row.Kind == LyricsOverrideKind.NoLyrics
                || row.IsInstrumental
                || string.IsNullOrWhiteSpace(row.PlainLyrics))
            {
                continue;
            }

            NameRow? names = cacheById.TryGetValue(row.TrackId, out CacheRow? cache)
                ? new NameRow(cache.TrackId, cache.TrackName, cache.ArtistName, cache.AlbumName)
                : fallbackNames.GetValueOrDefault(row.TrackId);
            entries.Add(new LyricsCorpusEntry(
                row.TrackId,
                FirstNonEmpty(row.TrackName, names?.TrackName),
                FirstNonEmpty(row.ArtistName, names?.ArtistName),
                string.IsNullOrWhiteSpace(row.AlbumName) ? names?.AlbumName : row.AlbumName,
                row.PlainLyrics,
                null));
        }

        foreach (CacheRow row in cacheRows)
        {
            if (shadowed.Contains(row.TrackId) || (row.ExpiresAtUtc is { } expires && expires <= now))
            {
                continue;
            }

            entries.Add(new LyricsCorpusEntry(
                row.TrackId,
                row.TrackName,
                row.ArtistName,
                row.AlbumName,
                row.PlainLyrics,
                row.ExpiresAtUtc));
        }

        return entries;
    }

    /// <summary>
    /// Names of cache rows the positive projection skipped (a negative or instrumental
    /// row), for overrides that were saved without a title or artist.
    /// </summary>
    private async Task<Dictionary<string, NameRow>> LoadFallbackNamesAsync(
        List<OverrideRow> overrides,
        Dictionary<string, CacheRow> cacheById,
        CancellationToken cancellationToken)
    {
        List<string> missing = overrides
            .Where(o => o.Kind != LyricsOverrideKind.NoLyrics
                && (string.IsNullOrWhiteSpace(o.TrackName) || string.IsNullOrWhiteSpace(o.ArtistName))
                && !cacheById.ContainsKey(o.TrackId))
            .Select(o => o.TrackId)
            .ToList();
        if (missing.Count == 0)
        {
            return new Dictionary<string, NameRow>(StringComparer.Ordinal);
        }

        List<NameRow> names = await _dbContext.CachedLyrics
            .AsNoTracking()
            .Where(c => missing.Contains(c.TrackId))
            .Select(c => new NameRow(c.TrackId, c.TrackName, c.ArtistName, c.AlbumName))
            .ToListAsync(cancellationToken);
        return names.ToDictionary(n => n.TrackId, StringComparer.Ordinal);
    }

    private static string FirstNonEmpty(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback ?? string.Empty;

    private sealed record OverrideRow(
        string TrackId,
        LyricsOverrideKind Kind,
        string? TrackName,
        string? ArtistName,
        string? AlbumName,
        string? PlainLyrics,
        bool IsInstrumental);

    private sealed record CacheRow(
        string TrackId,
        string TrackName,
        string ArtistName,
        string? AlbumName,
        string PlainLyrics,
        DateTimeOffset? ExpiresAtUtc);

    private sealed record NameRow(string TrackId, string TrackName, string ArtistName, string? AlbumName);
}
