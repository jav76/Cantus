using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLyricsOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LyricsOverrides",
                columns: table => new
                {
                    TrackId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ProviderCandidateId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TrackName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ArtistName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AlbumName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PlainLyrics = table.Column<string>(type: "TEXT", nullable: true),
                    RawSyncedLrc = table.Column<string>(type: "TEXT", nullable: true),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsInstrumental = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LyricsOverrides", x => x.TrackId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LyricsOverrides");
        }
    }
}
