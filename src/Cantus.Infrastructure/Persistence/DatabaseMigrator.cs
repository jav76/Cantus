using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations at startup, first copying an existing database aside. SQLite table
/// rebuilds run as several transactions, so a crash between them leaves a database that only a
/// restore can fix.
/// </summary>
public static class DatabaseMigrator
{
    public const int BACKUPS_TO_KEEP = 3;
    public const string BACKUP_INFIX = ".pre-";
    public const string BACKUP_SUFFIX = ".bak";

    /// <summary>Returns the backup taken, or null when there was nothing to protect.</summary>
    public static string? Migrate(CantusDbContext context, bool backupBeforeMigrating, ILogger logger)
    {
        string? backupPath = backupBeforeMigrating ? BackupIfNeeded(context, logger) : null;
        context.Database.Migrate();
        return backupPath;
    }

    private static string? BackupIfNeeded(CantusDbContext context, ILogger logger)
    {
        string? connectionString = context.Database.GetConnectionString();
        string? databasePath = GetDatabaseFilePath(connectionString);
        if (connectionString is null || databasePath is null || !File.Exists(databasePath))
        {
            return null;
        }

        List<string> pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count == 0 || !context.Database.GetAppliedMigrations().Any())
        {
            return null;
        }

        string target = pending[^1];
        string backupPath = $"{databasePath}{BACKUP_INFIX}{target}{BACKUP_SUFFIX}";

        // A backup for the same target means an earlier attempt did not finish: it holds the
        // last good copy, and the database beside it may be half migrated.
        if (File.Exists(backupPath))
        {
            logger.LogWarning(
                "Keeping the existing backup {BackupPath} taken before an earlier attempt to migrate to {Migration}",
                backupPath,
                target);
            return backupPath;
        }

        try
        {
            using SqliteConnection source = new(connectionString);
            using SqliteConnection destination = new(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Pooling = false
            }.ToString());
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
        catch (Exception ex)
        {
            TryDelete(backupPath, logger);
            throw new InvalidOperationException(
                $"Could not back up the database to {backupPath} before migrating to {target}. "
                + "Free some disk space, or set Database__BackupBeforeMigrations=false to migrate without a backup.",
                ex);
        }

        logger.LogInformation(
            "Backed up the database to {BackupPath} before applying {Count} migration(s) up to {Migration}",
            backupPath,
            pending.Count,
            target);
        PruneOldBackups(databasePath, logger);
        return backupPath;
    }

    internal static string? GetDatabaseFilePath(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        SqliteConnectionStringBuilder builder = new(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || builder.Mode == SqliteOpenMode.Memory
            || builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFullPath(builder.DataSource);
    }

    /// <summary>Migration ids start with a timestamp, so ordering by name orders by age.</summary>
    private static void PruneOldBackups(string databasePath, ILogger logger)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (directory is null)
        {
            return;
        }

        string pattern = $"{Path.GetFileName(databasePath)}{BACKUP_INFIX}*{BACKUP_SUFFIX}";
        IEnumerable<string> stale = Directory.GetFiles(directory, pattern)
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Skip(BACKUPS_TO_KEEP);

        foreach (string path in stale)
        {
            TryDelete(path, logger);
        }
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete database backup {BackupPath}", path);
        }
    }
}
