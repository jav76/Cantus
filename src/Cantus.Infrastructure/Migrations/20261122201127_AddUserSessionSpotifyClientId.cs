using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessionSpotifyClientId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpotifyClientId",
                table: "UserSessions",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpotifyClientId",
                table: "UserSessions");
        }
    }
}
