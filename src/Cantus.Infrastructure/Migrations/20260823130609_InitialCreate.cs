using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CachedLyrics",
                columns: table => new
                {
                    TrackId = table.Column<string>(type: "TEXT", nullable: false),
                    TrackName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ArtistName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AlbumName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    PlainLyrics = table.Column<string>(type: "TEXT", nullable: true),
                    RawSyncedLrc = table.Column<string>(type: "TEXT", nullable: true),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsInstrumental = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNotFound = table.Column<bool>(type: "INTEGER", nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastAccessedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedLyrics", x => x.TrackId);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    RoomCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    HostUserId = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.RoomCode);
                });

            migrationBuilder.CreateTable(
                name: "TrackOffsets",
                columns: table => new
                {
                    TrackId = table.Column<string>(type: "TEXT", nullable: false),
                    OffsetMs = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackOffsets", x => x.TrackId);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SpotifyUserId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    EncryptedAccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CachedLyrics_ArtistName_TrackName",
                table: "CachedLyrics",
                columns: new[] { "ArtistName", "TrackName" });

            migrationBuilder.CreateIndex(
                name: "IX_CachedLyrics_ExpiresAtUtc",
                table: "CachedLyrics",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_HostUserId",
                table: "Rooms",
                column: "HostUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_SpotifyUserId",
                table: "UserSessions",
                column: "SpotifyUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CachedLyrics");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "TrackOffsets");

            migrationBuilder.DropTable(
                name: "UserSessions");
        }
    }
}
