using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWordTimedLyricsOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WordTimedLinesJson",
                table: "LyricsOverrides",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WordTimedLinesJson",
                table: "LyricsOverrides");
        }
    }
}
