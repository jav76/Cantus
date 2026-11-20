using System;
using System.Text;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions WordTimedJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CantusDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public SqliteLyricsCacheRepository(CantusDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<bool> TryStartWordTimingUpgradeAsync(
        string trackId,
        int maxAttempts,
        TimeSpan retryInterval,
        CancellationToken cancellationToken = default)
    {
        CachedLyricsEntity? entity = await _dbContext.CachedLyrics
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TrackId == trackId, cancellationToken);

        if (entity is null || entity.IsNotFound || entity.WordTimingUpgradeAttempts >= maxAttempts)
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset lastChecked = entity.WordTimingCheckedAtUtc ?? entity.FetchedAtUtc;
        if (now - lastChecked < retryInterval)
        {
            return false;
        }

        // Compare-and-swap on the attempt count, so when two scopes pass the checks above for the same
        // track only the first UPDATE matches and only one of them queries the providers.
        int observedAttempts = entity.WordTimingUpgradeAttempts;
        int rowsAffected = await _dbContext.CachedLyrics
            .Where(c => c.TrackId == trackId
                && !c.IsNotFound
                && c.WordTimingUpgradeAttempts == observedAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.WordTimingUpgradeAttempts, c => c.WordTimingUpgradeAttempts + 1)
                    .SetProperty(c => c.WordTimingCheckedAtUtc, now),
                cancellationToken);

        await RefreshTrackedEntryAsync(trackId, cancellationToken);
        return rowsAffected == 1;
    }

    /// <summary>
    /// ExecuteUpdate bypasses the change tracker, so a row this context already tracks would keep stale
    /// upgrade counters and a later save could skip resetting them.
    /// </summary>
    private async Task RefreshTrackedEntryAsync(string trackId, CancellationToken cancellationToken)
    {
        CachedLyricsEntity? tracked = _dbContext.CachedLyrics.Local.FirstOrDefault(c => c.TrackId == trackId);
        if (tracked is not null)
        {
            await _dbContext.Entry(tracked).ReloadAsync(cancellationToken);
        }
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
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

        IReadOnlyList<LyricLine>? wordTimedLines = DeserializeWordTimedLines(entity.WordTimedLinesJson);
        if (wordTimedLines is not null)
        {
            return new SyncedLyrics
            {
                TrackId = entity.TrackId,
                Title = entity.TrackName,
                Artist = entity.ArtistName,
                Album = entity.AlbumName,
                Lines = wordTimedLines,
                IsSynced = wordTimedLines.Count > 0,
                IsInstrumental = false,
                PlainLyrics = entity.PlainLyrics ?? string.Join("\n", wordTimedLines.Select(l => l.Text))
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
        DateTimeOffset now = _timeProvider.GetUtcNow();
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
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
        entity.WordTimedLinesJson = lyrics.HasWordTiming ? SerializeWordTimedLines(lyrics.Lines) : null;
        entity.WordTimingUpgradeAttempts = 0;
        entity.WordTimingCheckedAtUtc = null;
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
        DateTimeOffset now = _timeProvider.GetUtcNow();
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
        entity.WordTimedLinesJson = null;
        entity.WordTimingUpgradeAttempts = 0;
        entity.WordTimingCheckedAtUtc = null;
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

    public async Task<LyricsOverride?> GetLyricsOverrideAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        LyricsOverrideEntity? entity = await _dbContext.LyricsOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TrackId == trackId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        if (entity.Kind == LyricsOverrideKind.NoLyrics)
        {
            return LyricsOverride.ForNoLyrics(entity.TrackId);
        }

        string trackName = entity.TrackName ?? string.Empty;
        string artistName = entity.ArtistName ?? string.Empty;
        SyncedLyrics lyrics = entity.IsInstrumental
            ? new SyncedLyrics
            {
                TrackId = entity.TrackId,
                Title = trackName,
                Artist = artistName,
                Album = entity.AlbumName,
                Lines = [],
                IsSynced = false,
                IsInstrumental = true,
                PlainLyrics = null
            }
            : LrcParser.Parse(
                entity.RawSyncedLrc,
                entity.TrackId,
                trackName,
                artistName,
                entity.AlbumName,
                entity.PlainLyrics);

        return new LyricsOverride(
            entity.TrackId,
            entity.Kind,
            entity.Provider,
            entity.ProviderCandidateId,
            lyrics);
    }

    public async Task SaveLyricsOverrideAsync(
        LyricsOverride lyricsOverride,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lyricsOverride);

        LyricsOverrideEntity? entity = await _dbContext.LyricsOverrides
            .FirstOrDefaultAsync(o => o.TrackId == lyricsOverride.TrackId, cancellationToken);

        if (entity is null)
        {
            entity = new LyricsOverrideEntity
            {
                TrackId = lyricsOverride.TrackId,
                Kind = lyricsOverride.Kind
            };
            await _dbContext.LyricsOverrides.AddAsync(entity, cancellationToken);
        }

        SyncedLyrics? lyrics = lyricsOverride.Lyrics;
        entity.Kind = lyricsOverride.Kind;
        entity.Provider = lyricsOverride.Provider;
        entity.ProviderCandidateId = lyricsOverride.ProviderCandidateId;
        entity.TrackName = lyrics?.Title;
        entity.ArtistName = lyrics?.Artist;
        entity.AlbumName = lyrics?.Album;
        entity.PlainLyrics = lyrics?.PlainLyrics;
        entity.RawSyncedLrc = lyrics is null ? null : GenerateRawLrc(lyrics);
        entity.IsSynced = lyrics?.IsSynced ?? false;
        entity.IsInstrumental = lyrics?.IsInstrumental ?? false;
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow();

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLyricsOverrideAsync(string trackId, CancellationToken cancellationToken = default)
    {
        LyricsOverrideEntity? entity = await _dbContext.LyricsOverrides
            .FirstOrDefaultAsync(o => o.TrackId == trackId, cancellationToken);

        if (entity is not null)
        {
            _dbContext.LyricsOverrides.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
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

    private static string SerializeWordTimedLines(IReadOnlyList<LyricLine> lines)
    {
        List<StoredLine> stored = lines
            .Select(line => new StoredLine(
                (long)line.Timestamp.TotalMilliseconds,
                line.Text,
                line.Syllables?
                    .Select(s => new StoredSyllable(
                        (long)s.Timestamp.TotalMilliseconds,
                        (long)s.Duration.TotalMilliseconds,
                        s.Text))
                    .ToList()))
            .ToList();

        return JsonSerializer.Serialize(stored, WordTimedJsonOptions);
    }

    /// <summary>Returns null when there is no stored JSON or it cannot be read, so the caller falls back to LRC.</summary>
    private static IReadOnlyList<LyricLine>? DeserializeWordTimedLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        List<StoredLine>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<List<StoredLine>>(json, WordTimedJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return stored?
            .Select(line => new LyricLine(
                TimeSpan.FromMilliseconds(line.TimestampMs),
                line.Text ?? string.Empty,
                line.Syllables is { Count: > 0 }
                    ? line.Syllables
                        .Select(s => new LyricSyllable(
                            TimeSpan.FromMilliseconds(s.TimestampMs),
                            TimeSpan.FromMilliseconds(s.DurationMs),
                            s.Text ?? string.Empty))
                        .ToList()
                    : null))
            .ToList();
    }

    private sealed record StoredLine(long TimestampMs, string? Text, List<StoredSyllable>? Syllables);

    private sealed record StoredSyllable(long TimestampMs, long DurationMs, string? Text);

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
