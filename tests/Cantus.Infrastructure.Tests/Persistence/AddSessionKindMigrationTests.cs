using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Cantus.Infrastructure.Tests.Persistence;

/// <summary>
/// AddSessionKind is the first SQLite table rebuild in the repo: making the Spotify columns
/// nullable recreates UserSessions, and Displays has a cascading foreign key to it. These tests
/// run the real migrations on a file database and check that existing rows and the foreign key
/// survive the rebuild.
/// </summary>
public sealed class AddSessionKindMigrationTests : IDisposable
{
    private const string PREVIOUS_MIGRATION = "AddUserSessionSpotifyClientId";
    private const string SESSION_ID = "session-1";
    private const string SPOTIFY_USER_ID = "spotify-user-1";
    private const string OWN_APP_CLIENT_ID = "0123456789abcdef0123456789abcdef";
    private const string DISPLAY_ID = "display-1";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"cantus-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task MigrateToHead_KeepsExistingSessionAndDisplay()
    {
        await MigrateAsync(PREVIOUS_MIGRATION);
        await SeedPreviousSchemaAsync();

        await MigrateAsync(null);

        await using SqliteConnection connection = await OpenAsync();
        (await ScalarAsync<long>(connection, $"SELECT COUNT(*) FROM \"Displays\" WHERE \"Id\" = '{DISPLAY_ID}'"))
            .Should().Be(1);
        (await ScalarAsync<string>(connection, $"SELECT \"Kind\" FROM \"UserSessions\" WHERE \"Id\" = '{SESSION_ID}'"))
            .Should().Be("Spotify");
        (await ScalarAsync<string>(connection, $"SELECT \"SpotifyUserId\" FROM \"UserSessions\" WHERE \"Id\" = '{SESSION_ID}'"))
            .Should().Be(SPOTIFY_USER_ID);
        (await ScalarAsync<string>(connection, $"SELECT \"SpotifyClientId\" FROM \"UserSessions\" WHERE \"Id\" = '{SESSION_ID}'"))
            .Should().Be(OWN_APP_CLIENT_ID, "the table rebuild must copy the own-app Client ID, or refresh breaks");
        (await ScalarAsync<string>(connection, $"SELECT \"EncryptedRefreshToken\" FROM \"UserSessions\" WHERE \"Id\" = '{SESSION_ID}'"))
            .Should().Be("enc-refresh");
    }

    [Fact]
    public async Task MigrateToHead_LeavesNoForeignKeyViolations()
    {
        await MigrateAsync(PREVIOUS_MIGRATION);
        await SeedPreviousSchemaAsync();

        await MigrateAsync(null);

        await using SqliteConnection connection = await OpenAsync();
        await using SqliteCommand check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check;";
        await using SqliteDataReader reader = await check.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeFalse("the rebuild must not orphan any Display");
    }

    [Fact]
    public async Task MigrateToHead_StillEnforcesAndCascadesTheDisplayForeignKey()
    {
        await MigrateAsync(PREVIOUS_MIGRATION);
        await SeedPreviousSchemaAsync();

        await MigrateAsync(null);

        await using SqliteConnection connection = await OpenAsync();
        Func<Task> orphan = () => ExecuteAsync(
            connection,
            "INSERT INTO \"Displays\" (\"Id\", \"OwnerSessionId\", \"Name\", \"TokenHash\", \"CreatedAtUtc\") "
            + "VALUES ('orphan', 'missing-session', 'TV', 'hash-orphan', '2026-01-01 00:00:00+00:00');");
        await orphan.Should().ThrowAsync<SqliteException>("the Displays foreign key must still be in force");

        await ExecuteAsync(connection, $"DELETE FROM \"UserSessions\" WHERE \"Id\" = '{SESSION_ID}';");
        (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM \"Displays\""))
            .Should().Be(0, "deleting a Session still cascades to its Displays");
    }

    [Fact]
    public async Task MigrateToHead_AllowsSeveralLocalSessionsWithoutSpotifyFields()
    {
        await MigrateAsync(PREVIOUS_MIGRATION);
        await SeedPreviousSchemaAsync();

        await MigrateAsync(null);

        await using SqliteConnection connection = await OpenAsync();
        for (int i = 0; i < 2; i++)
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO \"UserSessions\" (\"Id\", \"Kind\", \"DisplayName\", \"ExpiresAtUtc\", \"CreatedAtUtc\", \"UpdatedAtUtc\") "
                + $"VALUES ('local-{i}', 'Local', 'Desktop', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00');");
        }

        (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM \"UserSessions\" WHERE \"SpotifyUserId\" IS NULL"))
            .Should().Be(2);
    }

    private async Task MigrateAsync(string? targetMigration)
    {
        DbContextOptions<CantusDbContext> options = new DbContextOptionsBuilder<CantusDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        await using CantusDbContext context = new(options);
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private async Task SeedPreviousSchemaAsync()
    {
        await using SqliteConnection connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO \"UserSessions\" (\"Id\", \"SpotifyUserId\", \"DisplayName\", \"Email\", \"ProfileImageUrl\", "
            + "\"EncryptedAccessToken\", \"EncryptedRefreshToken\", \"SpotifyClientId\", \"ExpiresAtUtc\", \"CreatedAtUtc\", \"UpdatedAtUtc\") "
            + $"VALUES ('{SESSION_ID}', '{SPOTIFY_USER_ID}', 'Owner', NULL, NULL, 'enc-access', 'enc-refresh', '{OWN_APP_CLIENT_ID}', "
            + "'2026-01-01 01:00:00+00:00', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00');");
        await ExecuteAsync(
            connection,
            "INSERT INTO \"Displays\" (\"Id\", \"OwnerSessionId\", \"Name\", \"TokenHash\", \"CreatedAtUtc\") "
            + $"VALUES ('{DISPLAY_ID}', '{SESSION_ID}', 'TV', 'hash-1', '2026-01-01 00:00:00+00:00');");
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        SqliteConnection connection = new($"Data Source={_databasePath};Foreign Keys=True");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"No value returned by: {sql}");
        return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
