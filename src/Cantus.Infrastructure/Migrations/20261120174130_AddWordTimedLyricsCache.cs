using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWordTimedLyricsCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WordTimedLinesJson",
                table: "CachedLyrics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WordTimingCheckedAtUtc",
                table: "CachedLyrics",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WordTimingUpgradeAttempts",
                table: "CachedLyrics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WordTimedLinesJson",
                table: "CachedLyrics");

            migrationBuilder.DropColumn(
                name: "WordTimingCheckedAtUtc",
                table: "CachedLyrics");

            migrationBuilder.DropColumn(
                name: "WordTimingUpgradeAttempts",
                table: "CachedLyrics");
        }
    }
}
