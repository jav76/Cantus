using System.Text;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Parsers;
using Cantus.Infrastructure.Persistence;
using Cantus.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cantus.Infrastructure.Lyrics;

public sealed class SqliteLyricsCacheRepository : ILyricsCacheRepository
{
    private readonly CantusDbContext _dbContext;

    public SqliteLyricsCacheRepository(CantusDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SyncedLyrics?> GetCachedLyricsAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CachedLyrics
            .FirstOrDefaultAsync(c => c.TrackId == trackId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (entity.ExpiresAtUtc.HasValue && entity.ExpiresAtUtc.Value <= now)
        {
            // Expired cache entry
            _dbContext.CachedLyrics.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (entity.IsNotFound)
        {
            return null;
        }

        // Update last accessed
        entity.LastAccessedAtUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (entity.IsInstrumental)
        {
            return new SyncedLyrics
            {
                TrackId = entity.TrackId,
                Title = entity.TrackName,
                Artist = entity.ArtistName,
                Album = entity.AlbumName,
                Lines = [],
                IsSynced = false,
                IsInstrumental = true,
                PlainLyrics = null
            };
        }

        var parsed = LrcParser.Parse(
            entity.RawSyncedLrc,
            entity.TrackId,
            entity.TrackName,
            entity.ArtistName,
            entity.AlbumName,
            entity.PlainLyrics);

        return parsed;
    }

    public async Task<bool> IsMarkedNotFoundAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.CachedLyrics
            .Where(c => c.TrackId == trackId)
            .Select(c => new { c.IsNotFound, c.ExpiresAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null || !entry.IsNotFound)
        {
            return false;
        }

        if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        return true;
    }

    public async Task SaveLyricsAsync(SyncedLyrics lyrics, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lyrics);

        var now = DateTimeOffset.UtcNow;
        var existing = await _dbContext.CachedLyrics
            .FirstOrDefaultAsync(c => c.TrackId == lyrics.TrackId, cancellationToken);

        string? rawLrc = GenerateRawLrc(lyrics);

        if (existing is not null)
        {
            existing.TrackName = lyrics.Title;
            existing.ArtistName = lyrics.Artist;
            existing.AlbumName = lyrics.Album;
            existing.PlainLyrics = lyrics.PlainLyrics;
            existing.RawSyncedLrc = rawLrc;
            existing.IsSynced = lyrics.IsSynced;
            existing.IsInstrumental = lyrics.IsInstrumental;
            existing.IsNotFound = false;
            existing.FetchedAtUtc = now;
            existing.LastAccessedAtUtc = now;
            existing.ExpiresAtUtc = timeToLive.HasValue ? now.Add(timeToLive.Value) : null;
        }
        else
        {
            var entity = new CachedLyricsEntity
            {
                TrackId = lyrics.TrackId,
                TrackName = lyrics.Title,
                ArtistName = lyrics.Artist,
                AlbumName = lyrics.Album,
                DurationMs = 0,
                PlainLyrics = lyrics.PlainLyrics,
                RawSyncedLrc = rawLrc,
                IsSynced = lyrics.IsSynced,
                IsInstrumental = lyrics.IsInstrumental,
                IsNotFound = false,
                FetchedAtUtc = now,
                LastAccessedAtUtc = now,
                ExpiresAtUtc = timeToLive.HasValue ? now.Add(timeToLive.Value) : null
            };

            await _dbContext.CachedLyrics.AddAsync(entity, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkNotFoundAsync(
        string trackId,
        string trackName,
        string artistName,
        string albumName,
        int durationMs,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _dbContext.CachedLyrics
            .FirstOrDefaultAsync(c => c.TrackId == trackId, cancellationToken);

        if (existing is not null)
        {
            existing.TrackName = trackName;
            existing.ArtistName = artistName;
            existing.AlbumName = albumName;
            existing.DurationMs = durationMs;
            existing.IsNotFound = true;
            existing.IsSynced = false;
            existing.IsInstrumental = false;
            existing.PlainLyrics = null;
            existing.RawSyncedLrc = null;
            existing.FetchedAtUtc = now;
            existing.LastAccessedAtUtc = now;
            existing.ExpiresAtUtc = now.Add(timeToLive);
        }
        else
        {
            var entity = new CachedLyricsEntity
            {
                TrackId = trackId,
                TrackName = trackName,
                ArtistName = artistName,
                AlbumName = albumName,
                DurationMs = durationMs,
                IsNotFound = true,
                IsSynced = false,
                IsInstrumental = false,
                PlainLyrics = null,
                RawSyncedLrc = null,
                FetchedAtUtc = now,
                LastAccessedAtUtc = now,
                ExpiresAtUtc = now.Add(timeToLive)
            };

            await _dbContext.CachedLyrics.AddAsync(entity, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetTrackOffsetAsync(string trackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return 0;
        }

        var entity = await _dbContext.TrackOffsets
            .FirstOrDefaultAsync(t => t.TrackId == trackId, cancellationToken);

        return entity?.OffsetMs ?? 0;
    }

    public async Task SetTrackOffsetAsync(string trackId, int offsetMs, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return;
        }

        var entity = await _dbContext.TrackOffsets
            .FirstOrDefaultAsync(t => t.TrackId == trackId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (entity is not null)
        {
            entity.OffsetMs = offsetMs;
            entity.UpdatedAtUtc = now;
        }
        else
        {
            entity = new TrackOffsetEntity
            {
                TrackId = trackId,
                OffsetMs = offsetMs,
                UpdatedAtUtc = now
            };
            await _dbContext.TrackOffsets.AddAsync(entity, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? GenerateRawLrc(SyncedLyrics lyrics)
    {
        if (lyrics.Lines.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var line in lyrics.Lines)
        {
            int min = (int)line.Timestamp.TotalMinutes;
            int sec = line.Timestamp.Seconds;
            int csec = line.Timestamp.Milliseconds / 10;
            sb.AppendFormat("[{0:D2}:{1:D2}.{2:D2}]{3}\n", min, sec, csec, line.Text);
        }

        return sb.ToString();
    }
}

