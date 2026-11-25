using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LocalServerFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cantus-local-server-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, "Cantus.Server")]
    [InlineData(true, "Cantus.Server.exe")]
    public void Find_ServerNextToTheApp_PointsAtItAndThePerUserDataFolder(bool isWindows, string executableName)
    {
        string appDirectory = Path.Combine(_root, "app");
        string serverDirectory = Path.Combine(appDirectory, LocalServerBundle.SERVER_FOLDER);
        Directory.CreateDirectory(serverDirectory);
        File.WriteAllText(Path.Combine(serverDirectory, executableName), string.Empty);

        LocalServerBundle? bundle = LocalServerBundle.Find(appDirectory, Path.Combine(_root, "data"), isWindows);

        bundle.Should().NotBeNull();
        bundle!.ExecutablePath.Should().Be(Path.Combine(serverDirectory, executableName));
        bundle.ServerDirectory.Should().Be(serverDirectory);
        bundle.DataDirectory.Should().Be(Path.Combine(_root, "data", "Cantus", "server"));
        bundle.DatabasePath.Should().Be(Path.Combine(bundle.DataDirectory, "cantus.db"));
    }

    [Fact]
    public void Find_NoServerFolder_IsNull()
    {
        Directory.CreateDirectory(_root);

        LocalServerBundle.Find(_root, Path.Combine(_root, "data"), isWindows: false).Should().BeNull();
    }

    [Fact]
    public void StateStore_RoundTripsAndClearsTheControlToken()
    {
        LocalServerStateStore store = new(Path.Combine(_root, "data"));

        store.ReadControlToken().Should().BeNull();

        store.SaveControlToken("secret-token");
        new LocalServerStateStore(Path.Combine(_root, "data")).ReadControlToken().Should().Be("secret-token");

        store.Clear();
        store.ReadControlToken().Should().BeNull();
    }

    [Fact]
    public void StateStore_OnUnix_KeepsTheTokenPrivateToTheUser()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string dataDirectory = Path.Combine(_root, "data");
        new LocalServerStateStore(dataDirectory).SaveControlToken("secret-token");

        File.GetUnixFileMode(Path.Combine(dataDirectory, LocalServerStateStore.STATE_FILE))
            .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
