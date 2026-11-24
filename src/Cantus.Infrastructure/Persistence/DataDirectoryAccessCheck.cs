using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Cantus.Infrastructure.Persistence;

/// <summary>
/// Finds data files the server cannot use before anything opens them. Older images ran as root,
/// so a volume they created holds root-owned files that the non-root image cannot write, and
/// SQLite only reports that as "attempt to write a readonly database" deep inside a migration.
/// </summary>
public static class DataDirectoryAccessCheck
{
    public const string KEYS_DIRECTORY_NAME = "keys";

    /// <summary>Paths that exist but cannot be written (directories, the database) or read (keys).</summary>
    public static IReadOnlyList<string> FindInaccessiblePaths(string? dataDirectory, string? databaseConnectionString)
    {
        List<string> problems = [];
        string? databasePath = DatabaseMigrator.GetDatabaseFilePath(databaseConnectionString);

        if (!string.IsNullOrEmpty(dataDirectory))
        {
            string dataPath = Path.GetFullPath(dataDirectory);
            CheckDirectoryWritable(dataPath, problems);

            string keysPath = Path.Combine(dataPath, KEYS_DIRECTORY_NAME);
            if (CheckDirectoryWritable(keysPath, problems))
            {
                problems.AddRange(Directory.EnumerateFiles(keysPath).Where(file => !CanRead(file)));
            }
        }

        if (databasePath is not null)
        {
            string? databaseDirectory = Path.GetDirectoryName(databasePath);
            if (databaseDirectory is not null)
            {
                CheckDirectoryWritable(databaseDirectory, problems);
            }

            if (File.Exists(databasePath) && !CanWriteDatabase(databasePath))
            {
                problems.Add(databasePath);
            }
        }

        return problems.Distinct().ToList();
    }

    /// <summary>One message an operator can act on without reading a stack trace.</summary>
    public static string BuildFixMessage(
        IReadOnlyList<string> inaccessiblePaths,
        string? dataDirectory,
        string? userId,
        bool runningInContainer)
    {
        string user = string.IsNullOrEmpty(userId) ? "the user Cantus runs as" : $"uid {userId}";
        string owner = string.IsNullOrEmpty(userId) ? "<uid>" : $"{userId}:{userId}";
        string directory = string.IsNullOrEmpty(dataDirectory) ? "<data directory>" : dataDirectory;

        string fix = runningInContainer
            ? $"""
              This usually means the volume was created by an older Cantus image that ran as root. Give it to {user}, then start Cantus again:
                docker compose run --rm --no-deps --user 0 --entrypoint chown cantus -R {owner} {directory}
              Without Compose, use your volume and image names:
                docker run --rm --user 0 -v <volume>:{directory} --entrypoint chown <image> -R {owner} {directory}
              """
            : $"""
              Give the data directory to {user}, then start Cantus again:
                sudo chown -R {owner} {directory}
              """;

        return $"""
            Cantus cannot use its data files. These paths are not writable (or, for keys, not readable) by {user}:
              {string.Join(Environment.NewLine + "  ", inaccessiblePaths)}
            {fix}
            See https://cantus.docs.jav26122.net/operator-guide/self-hosting/#upgrading-from-an-image-that-ran-as-root
            """;
    }

    /// <summary>Reads the effective uid on Linux, falling back to the image's APP_UID elsewhere.</summary>
    public static string? GetCurrentUserId()
    {
        const string statusPath = "/proc/self/status";
        try
        {
            if (File.Exists(statusPath))
            {
                string? uidLine = File.ReadLines(statusPath).FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal));
                string[]? fields = uidLine?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields is { Length: > 2 })
                {
                    return fields[2];
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not Linux enough to tell; APP_UID below is what the image runs as.
        }

        return Environment.GetEnvironmentVariable("APP_UID");
    }

    /// <returns>True when the directory exists and is writable.</returns>
    private static bool CheckDirectoryWritable(string directory, List<string> problems)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        string probe = Path.Combine(directory, $".cantus-write-check-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(directory);
            return false;
        }
    }

    /// <summary>
    /// Asks SQLite rather than opening the files directly: closing any descriptor to a file drops
    /// every POSIX lock this process holds on it, including those of open SQLite connections.
    /// SQLite falls back to read-only when it cannot write the file. For the same reason the -wal
    /// and -shm files are not checked: SQLite only notices they are read-only when it writes.
    /// </summary>
    private static bool CanWriteDatabase(string databasePath)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());

        try
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master";
            command.ExecuteScalar();
            return raw.sqlite3_db_readonly(connection.Handle, "main") == 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is raw.SQLITE_READONLY or raw.SQLITE_CANTOPEN or raw.SQLITE_PERM)
        {
            return false;
        }
    }

    private static bool CanRead(string file)
    {
        try
        {
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
