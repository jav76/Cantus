using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SpotifyUserId",
                table: "UserSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedRefreshToken",
                table: "UserSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedAccessToken",
                table: "UserSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "UserSessions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Spotify");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old schema needs a unique Spotify user id and tokens on every row, which Local
            // Sessions do not have. Their Displays go with them through the cascade.
            migrationBuilder.Sql("DELETE FROM \"UserSessions\" WHERE \"Kind\" = 'Local';");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "UserSessions");

            migrationBuilder.AlterColumn<string>(
                name: "SpotifyUserId",
                table: "UserSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedRefreshToken",
                table: "UserSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedAccessToken",
                table: "UserSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
