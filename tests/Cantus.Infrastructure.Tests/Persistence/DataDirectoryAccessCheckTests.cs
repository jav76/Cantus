using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Cantus.Infrastructure.Tests.Persistence;

public sealed class DataDirectoryAccessCheckTests : IDisposable
{
    private const UnixFileMode READ_ONLY_FILE = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode READ_ONLY_DIRECTORY = READ_ONLY_FILE
        | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"cantus-access-{Guid.NewGuid():N}");
    private readonly string _keysDirectory;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public DataDirectoryAccessCheckTests()
    {
        _keysDirectory = Path.Combine(_dataDirectory, DataDirectoryAccessCheck.KEYS_DIRECTORY_NAME);
        _databasePath = Path.Combine(_dataDirectory, "cantus.db");
        _connectionString = $"Data Source={_databasePath}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_dataDirectory))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(_dataDirectory, "*", SearchOption.AllDirectories).Prepend(_dataDirectory))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public void FindInaccessiblePaths_OnAFreshInstall_FindsNothing()
    {
        DataDirectoryAccessCheck.FindInaccessiblePaths(_dataDirectory, _connectionString).Should().BeEmpty();
    }

    [Fact]
    public void FindInaccessiblePaths_WithWritableFiles_FindsNothing()
    {
        CreateExistingInstall();

        DataDirectoryAccessCheck.FindInaccessiblePaths(_dataDirectory, _connectionString).Should().BeEmpty();
    }

    [Fact]
    public void FindInaccessiblePaths_ForAnInMemoryDatabase_ChecksOnlyTheDataDirectory()
    {
        DataDirectoryAccessCheck.FindInaccessiblePaths(null, "Data Source=:memory:").Should().BeEmpty();
    }

    [UnixNonRootFact]
    public void FindInaccessiblePaths_WithAReadOnlyDatabase_ReportsIt()
    {
        CreateExistingInstall();
        File.SetUnixFileMode(_databasePath, READ_ONLY_FILE);

        DataDirectoryAccessCheck.FindInaccessiblePaths(_dataDirectory, _connectionString)
            .Should().Equal(_databasePath);
    }

    [UnixNonRootFact]
    public void FindInaccessiblePaths_WithAReadOnlyDataDirectory_ReportsIt()
    {
        CreateExistingInstall();
        File.SetUnixFileMode(_dataDirectory, READ_ONLY_DIRECTORY);

        DataDirectoryAccessCheck.FindInaccessiblePaths(_dataDirectory, _connectionString)
            .Should().Equal(_dataDirectory);
    }

    [UnixNonRootFact]
    public void FindInaccessiblePaths_WithAnUnreadableKey_ReportsTheKey()
    {
        CreateExistingInstall();
        string keyPath = Path.Combine(_keysDirectory, "key-1.xml");
        File.SetUnixFileMode(keyPath, UnixFileMode.None);

        DataDirectoryAccessCheck.FindInaccessiblePaths(_dataDirectory, _connectionString)
            .Should().Equal(keyPath);
    }

    [UnixNonRootFact]
    public void FindInaccessiblePaths_WithAReadOnlyKeysDirectory_ReportsIt()
    {
        CreateExistingInstall();
        File.SetUnixFileMode(_keysDirectory, READ_ONLY_DIRECTORY);

        DataDirectoryAccessCheck.FindInaccessiblePaths(_dataDirectory, _connectionString)
            .Should().Equal(_keysDirectory);
    }

    [Fact]
    public void BuildFixMessage_InAContainer_GivesTheChownCommandForTheRunningUser()
    {
        string message = DataDirectoryAccessCheck.BuildFixMessage(
            ["/app/data/cantus.db", "/app/data/keys"], "/app/data", "1654", runningInContainer: true);

        message.Should().Contain("/app/data/cantus.db")
            .And.Contain("/app/data/keys")
            .And.Contain("uid 1654")
            .And.Contain("docker compose run --rm --no-deps --user 0 --entrypoint chown cantus -R 1654:1654 /app/data")
            .And.Contain("#upgrading-from-an-image-that-ran-as-root");
    }

    [Fact]
    public void BuildFixMessage_OutsideAContainer_GivesAPlainChown()
    {
        string message = DataDirectoryAccessCheck.BuildFixMessage(
            ["/srv/cantus/cantus.db"], "/srv/cantus", "1000", runningInContainer: false);

        message.Should().Contain("sudo chown -R 1000:1000 /srv/cantus").And.NotContain("docker");
    }

    private void CreateExistingInstall()
    {
        Directory.CreateDirectory(_keysDirectory);
        using (SqliteConnection connection = new($"{_connectionString};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE t(x)";
            command.ExecuteNonQuery();
        }

        File.WriteAllText(Path.Combine(_keysDirectory, "key-1.xml"), "<key />");
    }

    /// <summary>Permission bits mean nothing to root, and Windows has none to set.</summary>
    private sealed class UnixNonRootFactAttribute : FactAttribute
    {
        public UnixNonRootFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "Needs Unix file modes.";
            }
            else if (DataDirectoryAccessCheck.GetCurrentUserId() == "0")
            {
                Skip = "Root can write read-only files.";
            }
        }
    }
}
