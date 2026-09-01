using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    public async Task<SyncedLyrics?> GetCachedLyricsAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        CachedLyricsEntity? entity = await _dbContext.CachedLyrics
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TrackId == trackId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (entity.ExpiresAtUtc.HasValue && entity.ExpiresAtUtc.Value <= now)
        {
            return null;
        }

        if (entity.IsNotFound)
        {
            return null;
        }

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

        SyncedLyrics parsed = LrcParser.Parse(
            entity.RawSyncedLrc,
            entity.TrackId,
            entity.TrackName,
            entity.ArtistName,
            entity.AlbumName,
            entity.PlainLyrics);

        return parsed;
    }

    public async Task<bool> IsMarkedNotFoundAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CachedLyricsEntity? entry = await _dbContext.CachedLyrics
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TrackId == trackId, cancellationToken);

        if (entry is null || !entry.IsNotFound)
        {
            return false;
        }

        if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value <= now)
        {
            return false;
        }

        return true;
    }

    public async Task SaveLyricsAsync(
        SyncedLyrics lyrics,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lyrics);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CachedLyricsEntity entity = await GetOrCreateLyricsEntityAsync(
            lyrics.TrackId,
            lyrics.Title,
            lyrics.Artist,
            cancellationToken);

        entity.TrackName = lyrics.Title;
        entity.ArtistName = lyrics.Artist;
        entity.AlbumName = lyrics.Album;
        entity.PlainLyrics = lyrics.PlainLyrics;
        entity.RawSyncedLrc = GenerateRawLrc(lyrics);
        entity.IsSynced = lyrics.IsSynced;
        entity.IsInstrumental = lyrics.IsInstrumental;
        entity.IsNotFound = false;
        entity.FetchedAtUtc = now;
        entity.LastAccessedAtUtc = now;
        entity.ExpiresAtUtc = timeToLive.HasValue ? now.Add(timeToLive.Value) : null;

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
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CachedLyricsEntity entity = await GetOrCreateLyricsEntityAsync(
            trackId,
            trackName,
            artistName,
            cancellationToken);

        entity.TrackName = trackName;
        entity.ArtistName = artistName;
        entity.AlbumName = albumName;
        entity.DurationMs = durationMs;
        entity.IsNotFound = true;
        entity.IsSynced = false;
        entity.IsInstrumental = false;
        entity.PlainLyrics = null;
        entity.RawSyncedLrc = null;
        entity.FetchedAtUtc = now;
        entity.LastAccessedAtUtc = now;
        entity.ExpiresAtUtc = now.Add(timeToLive);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CachedLyricsEntity> GetOrCreateLyricsEntityAsync(
        string trackId,
        string trackName,
        string artistName,
        CancellationToken cancellationToken)
    {
        CachedLyricsEntity? existing = await _dbContext.CachedLyrics
            .FirstOrDefaultAsync(c => c.TrackId == trackId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        CachedLyricsEntity entity = new()
        {
            TrackId = trackId,
            TrackName = trackName,
            ArtistName = artistName
        };
        await _dbContext.CachedLyrics.AddAsync(entity, cancellationToken);
        return entity;
    }

    public async Task<int> GetTrackOffsetAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return 0;
        }

        TrackOffsetEntity? entity = await _dbContext.TrackOffsets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TrackId == trackId, cancellationToken);

        return entity?.OffsetMs ?? 0;
    }

    public async Task SetTrackOffsetAsync(
        string trackId,
        int offsetMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            return;
        }

        TrackOffsetEntity? entity = await _dbContext.TrackOffsets
            .FirstOrDefaultAsync(t => t.TrackId == trackId, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
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

        StringBuilder sb = new();
        foreach (LyricLine line in lyrics.Lines)
        {
            int min = (int)line.Timestamp.TotalMinutes;
            int sec = line.Timestamp.Seconds;
            int csec = line.Timestamp.Milliseconds / 10;
            sb.AppendFormat("[{0:D2}:{1:D2}.{2:D2}]", min, sec, csec);

            if (line.Syllables is not null && line.Syllables.Count > 0)
            {
                for (int i = 0; i < line.Syllables.Count; i++)
                {
                    LyricSyllable syl = line.Syllables[i];
                    int sylMin = (int)syl.Timestamp.TotalMinutes;
                    int sylSec = syl.Timestamp.Seconds;
                    int sylCsec = syl.Timestamp.Milliseconds / 10;
                    if (i > 0)
                    {
                        sb.Append(' ');
                    }
                    sb.AppendFormat("<{0:D2}:{1:D2}.{2:D2}>{3}", sylMin, sylSec, sylCsec, syl.Text);
                }
                sb.Append('\n');
            }
            else
            {
                sb.Append(line.Text);
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }
}
