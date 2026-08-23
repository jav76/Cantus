using Cantus.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cantus.Infrastructure.Persistence;

public class CantusDbContext : DbContext
{
    public CantusDbContext(DbContextOptions<CantusDbContext> options) : base(options)
    {
    }

    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();
    public DbSet<CachedLyricsEntity> CachedLyrics => Set<CachedLyricsEntity>();
    public DbSet<TrackOffsetEntity> TrackOffsets => Set<TrackOffsetEntity>();
    public DbSet<RoomEntity> Rooms => Set<RoomEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserSessionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SpotifyUserId).IsUnique();
            entity.Property(e => e.DisplayName).HasMaxLength(256);
            entity.Property(e => e.Email).HasMaxLength(256);
        });

        modelBuilder.Entity<CachedLyricsEntity>(entity =>
        {
            entity.HasKey(e => e.TrackId);
            entity.HasIndex(e => new { e.ArtistName, e.TrackName });
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.Property(e => e.TrackName).HasMaxLength(512);
            entity.Property(e => e.ArtistName).HasMaxLength(512);
            entity.Property(e => e.AlbumName).HasMaxLength(512);
        });

        modelBuilder.Entity<TrackOffsetEntity>(entity =>
        {
            entity.HasKey(e => e.TrackId);
        });

        modelBuilder.Entity<RoomEntity>(entity =>
        {
            entity.HasKey(e => e.RoomCode);
            entity.Property(e => e.RoomCode).HasMaxLength(32);
            entity.HasIndex(e => e.HostUserId);
        });
    }
}
