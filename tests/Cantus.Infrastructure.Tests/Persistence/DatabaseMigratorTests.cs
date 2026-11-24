using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cantus.Infrastructure.Tests.Persistence;

public sealed class DatabaseMigratorTests : IDisposable
{
    private const string PREVIOUS_MIGRATION = "AddUserSessionSpotifyClientId";
    private const string SESSION_ID = "session-1";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"cantus-migrator-{Guid.NewGuid():N}");
    private readonly string _databasePath;

    public DatabaseMigratorTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "cantus.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Migrate_ANewDatabase_TakesNoBackup()
    {
        string? backup = MigrateWithMigrator();

        backup.Should().BeNull();
        File.Exists(_databasePath).Should().BeTrue();
        Backups().Should().BeEmpty();
    }

    [Fact]
    public void Migrate_AnUpToDateDatabase_TakesNoBackup()
    {
        MigrateTo(null);

        string? backup = MigrateWithMigrator();

        backup.Should().BeNull();
        Backups().Should().BeEmpty();
    }

    [Fact]
    public void Migrate_WithPendingMigrations_BacksUpTheOldDatabaseBeforeMigrating()
    {
        MigrateTo(PREVIOUS_MIGRATION);
        SeedSession();

        string? backup = MigrateWithMigrator();

        backup.Should().NotBeNull();
        File.Exists(backup).Should().BeTrue();
        Path.GetFileName(backup).Should().StartWith("cantus.db.pre-").And.EndWith("_AddSessionKind.bak");

        using SqliteConnection old = Open(backup!);
        Scalar<string>(old, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1")
            .Should().EndWith(PREVIOUS_MIGRATION, "the backup is the database as it was before this upgrade");
        Scalar<long>(old, $"SELECT COUNT(*) FROM \"UserSessions\" WHERE \"Id\" = '{SESSION_ID}'").Should().Be(1);
        HasColumn(old, "UserSessions", "Kind").Should().BeFalse();

        using SqliteConnection migrated = Open(_databasePath);
        HasColumn(migrated, "UserSessions", "Kind").Should().BeTrue();
    }

    [Fact]
    public void Migrate_WhenABackupForTheSameTargetExists_KeepsIt()
    {
        MigrateTo(PREVIOUS_MIGRATION);
        string expectedBackup = BackupPathFor(LatestMigrationId());
        File.WriteAllText(expectedBackup, "the last good copy");

        string? backup = MigrateWithMigrator();

        backup.Should().Be(expectedBackup);
        File.ReadAllText(expectedBackup).Should().Be(
            "the last good copy",
            "a retry after a failed upgrade must not replace the good copy with the half-migrated one");
    }

    [Fact]
    public void Migrate_KeepsOnlyTheNewestBackups()
    {
        string[] older =
        {
            BackupPathFor("20200101000000_First"),
            BackupPathFor("20200201000000_Second"),
            BackupPathFor("20200301000000_Third"),
        };
        foreach (string path in older)
        {
            File.WriteAllText(path, "old");
        }

        MigrateTo(PREVIOUS_MIGRATION);
        string? backup = MigrateWithMigrator();

        Backups().Should().HaveCount(DatabaseMigrator.BACKUPS_TO_KEEP);
        Backups().Should().Contain(backup!);
        File.Exists(older[0]).Should().BeFalse("the oldest backup is pruned");
        File.Exists(older[2]).Should().BeTrue();
    }

    [Fact]
    public void Migrate_WithBackupsTurnedOff_MigratesWithoutOne()
    {
        MigrateTo(PREVIOUS_MIGRATION);

        string? backup = MigrateWithMigrator(backupBeforeMigrating: false);

        backup.Should().BeNull();
        Backups().Should().BeEmpty();
        PendingMigrations().Should().BeEmpty();
    }

    [Fact]
    public void Migrate_WhenTheBackupFails_DoesNotMigrate()
    {
        MigrateTo(PREVIOUS_MIGRATION);
        Directory.CreateDirectory(BackupPathFor(LatestMigrationId()));

        Action migrate = () => MigrateWithMigrator();

        migrate.Should().Throw<InvalidOperationException>().WithMessage("*Database__BackupBeforeMigrations*");
        PendingMigrations().Should().NotBeEmpty("an upgrade without its backup must not run");
    }

    private string? MigrateWithMigrator(bool backupBeforeMigrating = true)
    {
        using CantusDbContext context = CreateContext();
        return DatabaseMigrator.Migrate(context, backupBeforeMigrating, NullLogger.Instance);
    }

    private void MigrateTo(string? targetMigration)
    {
        using CantusDbContext context = CreateContext();
        context.GetService<IMigrator>().Migrate(targetMigration);
    }

    private List<string> PendingMigrations()
    {
        using CantusDbContext context = CreateContext();
        return context.Database.GetPendingMigrations().ToList();
    }

    private string LatestMigrationId()
    {
        using CantusDbContext context = CreateContext();
        return context.Database.GetMigrations().Last();
    }

    private CantusDbContext CreateContext() => new(new DbContextOptionsBuilder<CantusDbContext>()
        .UseSqlite($"Data Source={_databasePath}")
        .Options);

    private string BackupPathFor(string migrationId) =>
        $"{_databasePath}{DatabaseMigrator.BACKUP_INFIX}{migrationId}{DatabaseMigrator.BACKUP_SUFFIX}";

    private string[] Backups() =>
        Directory.GetFiles(_directory, $"cantus.db{DatabaseMigrator.BACKUP_INFIX}*{DatabaseMigrator.BACKUP_SUFFIX}");

    private void SeedSession()
    {
        using SqliteConnection connection = Open(_databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"UserSessions\" (\"Id\", \"SpotifyUserId\", \"DisplayName\", \"EncryptedAccessToken\", "
            + "\"EncryptedRefreshToken\", \"ExpiresAtUtc\", \"CreatedAtUtc\", \"UpdatedAtUtc\") "
            + $"VALUES ('{SESSION_ID}', 'spotify-user', 'Owner', 'enc-access', 'enc-refresh', "
            + "'2026-01-01 01:00:00+00:00', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00');";
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column) =>
        Scalar<long>(connection, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'") > 0;
}
