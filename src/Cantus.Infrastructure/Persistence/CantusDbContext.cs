using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cantus.Infrastructure.Persistence;

public class CantusDbContext : DbContext
{
    public CantusDbContext(DbContextOptions<CantusDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();
    public DbSet<CachedLyricsEntity> CachedLyrics => Set<CachedLyricsEntity>();
    public DbSet<TrackOffsetEntity> TrackOffsets => Set<TrackOffsetEntity>();
    public DbSet<LyricsOverrideEntity> LyricsOverrides => Set<LyricsOverrideEntity>();
    public DbSet<RoomEntity> Rooms => Set<RoomEntity>();
    public DbSet<LogEntryEntity> LogEntries => Set<LogEntryEntity>();
    public DbSet<DisplayEntity> Displays => Set<DisplayEntity>();

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

        modelBuilder.Entity<LyricsOverrideEntity>(entity =>
        {
            entity.HasKey(e => e.TrackId);
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Provider).HasMaxLength(64);
            entity.Property(e => e.ProviderCandidateId).HasMaxLength(128);
            entity.Property(e => e.TrackName).HasMaxLength(512);
            entity.Property(e => e.ArtistName).HasMaxLength(512);
            entity.Property(e => e.AlbumName).HasMaxLength(512);
        });

        modelBuilder.Entity<RoomEntity>(entity =>
        {
            entity.HasKey(e => e.RoomCode);
            entity.Property(e => e.RoomCode).HasMaxLength(32);
            entity.HasIndex(e => e.HostUserId);
        });

        modelBuilder.Entity<LogEntryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.TimestampUtc);
            entity.HasIndex(e => e.Level);
            entity.Property(e => e.Level).HasMaxLength(32);
            entity.Property(e => e.Logger).HasMaxLength(256);
            entity.Property(e => e.TraceId).HasMaxLength(128);
        });

        modelBuilder.Entity<DisplayEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(DisplayInfo.MAX_NAME_LENGTH);
            entity.Property(e => e.TokenHash).HasMaxLength(DisplayEntity.TOKEN_HASH_LENGTH);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasOne<UserSessionEntity>()
                .WithMany()
                .HasForeignKey(e => e.OwnerSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
