using System.Runtime.CompilerServices;
using Cantus.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// Keeps <see cref="LyricsSearchIndex"/> in step with every EF write to
/// <c>CachedLyrics</c> and <c>LyricsOverrides</c> (singleton, shared by all contexts).
/// The touched track ids are recorded per context while saving, because by the time
/// the save has completed the entries are already Unchanged or Detached. They reach
/// the index only after a successful save; a failed or cancelled save discards them.
/// </summary>
public sealed class LyricsCorpusChangeInterceptor : SaveChangesInterceptor
{
    private readonly LyricsSearchIndex _index;
    private readonly ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();

    public LyricsCorpusChangeInterceptor(LyricsSearchIndex index)
    {
        _index = index;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Publish(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Publish(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Discard(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        Discard(eventData.Context);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    private void Record(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        HashSet<string> trackIds = new(StringComparer.Ordinal);
        foreach (EntityEntry<CachedLyricsEntity> entry in context.ChangeTracker.Entries<CachedLyricsEntity>())
        {
            if (IsWrite(entry.State))
            {
                trackIds.Add(entry.Entity.TrackId);
            }
        }

        foreach (EntityEntry<LyricsOverrideEntity> entry in context.ChangeTracker.Entries<LyricsOverrideEntity>())
        {
            if (IsWrite(entry.State))
            {
                trackIds.Add(entry.Entity.TrackId);
            }
        }

        if (trackIds.Count > 0)
        {
            _pending.AddOrUpdate(context, trackIds);
        }
        else
        {
            _pending.Remove(context);
        }
    }

    private void Publish(DbContext? context)
    {
        if (context is not null && _pending.TryGetValue(context, out HashSet<string>? trackIds))
        {
            _pending.Remove(context);
            _index.MarkDirty(trackIds);
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null)
        {
            _pending.Remove(context);
        }
    }

    private static bool IsWrite(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;
}
