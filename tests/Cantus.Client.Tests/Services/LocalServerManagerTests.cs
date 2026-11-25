using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LocalServerManagerTests
{
    private const string LOCAL_URL = ServerAddress.DEFAULT_BASE_URL;
    private const string APP_VERSION = "1.2.0";
    private const string OLD_VERSION = "1.1.0";

    [Theory]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://127.0.0.1:6000", false)]
    [InlineData("http://192.168.0.10:5000", false)]
    public void Manages_OnlyThisComputersDefaultAddress(string baseUrl, bool expected)
    {
        new Fixture().Manager.Manages(baseUrl).Should().Be(expected);
    }

    [Fact]
    public async Task EnsureRunningAsync_NoBundledServer_StartsNothing()
    {
        Fixture fixture = new(isBundled: false);

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.NotManaged);
        fixture.Manager.IsBundled.Should().BeFalse();
        fixture.Process.Launches.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureRunningAsync_AnotherAddress_StartsNothing()
    {
        Fixture fixture = new();

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync("http://192.168.0.10:5000");

        result.Should().Be(LocalServerStartResult.NotManaged);
        fixture.Process.Launches.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureRunningAsync_NothingAnswering_StartsTheServerAndWaitsForIt()
    {
        Fixture fixture = new();
        fixture.Api.AnswersAfterStartPolls = 3;

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.Started);
        LocalServerLaunch launch = fixture.Process.Launches.Should().ContainSingle().Subject;
        launch.BaseUrl.Should().Be(LOCAL_URL);
        launch.Environment["ASPNETCORE_URLS"].Should().Be(LOCAL_URL);
        launch.Environment["DATA_DIR"].Should().Be(fixture.Bundle.DataDirectory);
        launch.Environment["ConnectionStrings__CantusDatabase"].Should().Be($"Data Source={fixture.Bundle.DatabasePath}");
        launch.Arguments.Should().Equal("--contentRoot", fixture.Bundle.ServerDirectory);
        fixture.State.ControlToken.Should().Be(launch.ControlToken).And.HaveLength(64);
    }

    [Fact]
    public async Task EnsureRunningAsync_SameVersionAnswering_LeavesItRunning()
    {
        Fixture fixture = new();
        fixture.Api.RunningVersion = APP_VERSION;
        fixture.State.ControlToken = "token";

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.AlreadyRunning);
        fixture.Process.Launches.Should().BeEmpty();
        fixture.Api.ShutdownTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureRunningAsync_OlderServerThisAppStarted_ReplacesIt()
    {
        Fixture fixture = new();
        fixture.Api.RunningVersion = OLD_VERSION;
        fixture.State.ControlToken = "old-token";

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.Started);
        fixture.Api.ShutdownTokens.Should().Equal("old-token");
        fixture.Process.Launches.Should().ContainSingle();
        fixture.State.ControlToken.Should().NotBe("old-token");
    }

    [Fact]
    public async Task EnsureRunningAsync_OlderServerSomeoneElseStarted_LeavesItRunning()
    {
        Fixture fixture = new();
        fixture.Api.RunningVersion = OLD_VERSION;

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.AlreadyRunning);
        fixture.Process.Launches.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureRunningAsync_ServerNeverAnswers_Fails()
    {
        Fixture fixture = new();
        fixture.Api.AnswersAfterStartPolls = int.MaxValue;

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.Failed);
        fixture.Delays.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EnsureRunningAsync_AnotherProgramOnTheAddress_FailsWithoutWaiting()
    {
        Fixture fixture = new();
        fixture.Process.IsFree = false;

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.Failed);
        fixture.Process.Launches.Should().BeEmpty();
        fixture.Delays.Should().Be(0);
    }

    [Fact]
    public async Task EnsureRunningAsync_ServerCannotBeStarted_Fails()
    {
        Fixture fixture = new();
        fixture.Process.StartFailure = new InvalidOperationException("missing");

        LocalServerStartResult result = await fixture.Manager.EnsureRunningAsync(LOCAL_URL);

        result.Should().Be(LocalServerStartResult.Failed);
    }

    [Fact]
    public async Task StopAsync_ServerThisAppStarted_StopsItAndForgetsTheToken()
    {
        Fixture fixture = new();
        fixture.Api.RunningVersion = APP_VERSION;
        fixture.State.ControlToken = "token";

        bool stopped = await fixture.Manager.StopAsync(LOCAL_URL);

        stopped.Should().BeTrue();
        fixture.Api.ShutdownTokens.Should().Equal("token");
        fixture.State.ControlToken.Should().BeNull();
    }

    [Fact]
    public async Task StopAsync_ServerSomeoneElseStarted_IsLeftRunning()
    {
        Fixture fixture = new();
        fixture.Api.RunningVersion = APP_VERSION;

        bool stopped = await fixture.Manager.StopAsync(LOCAL_URL);

        stopped.Should().BeFalse();
        fixture.Api.ShutdownTokens.Should().BeEmpty();
        fixture.Api.RunningVersion.Should().Be(APP_VERSION);
    }

    [Fact]
    public async Task StopAsync_ServerRefusesTheToken_KeepsTheToken()
    {
        Fixture fixture = new();
        fixture.Api.RunningVersion = APP_VERSION;
        fixture.Api.AcceptsShutdown = false;
        fixture.State.ControlToken = "stale";

        bool stopped = await fixture.Manager.StopAsync(LOCAL_URL);

        stopped.Should().BeFalse();
        fixture.State.ControlToken.Should().Be("stale");
    }

    [Fact]
    public async Task WhenStartedAsync_WaitsForTheLaunchBegunAtStartup()
    {
        Fixture fixture = new();

        fixture.Manager.BeginStartup(LOCAL_URL);
        await fixture.Manager.WhenStartedAsync();

        fixture.Process.Launches.Should().ContainSingle();
        fixture.Api.RunningVersion.Should().Be(APP_VERSION);
    }

    private sealed class Fixture
    {
        public Fixture(bool isBundled = true)
        {
            Api = new FakeApi(Process);
            Manager = new LocalServerManager(
                isBundled ? Bundle : null,
                Api,
                Process,
                isBundled ? State : null,
                APP_VERSION,
                (_, _) =>
                {
                    Delays++;
                    return Task.CompletedTask;
                });
        }

        public LocalServerBundle Bundle { get; } = new(
            Path.Combine("app", "server", "Cantus.Server"),
            Path.Combine("data", "Cantus", "server"));

        public FakeProcess Process { get; } = new();

        public FakeApi Api { get; }

        public FakeState State { get; } = new();

        public LocalServerManager Manager { get; }

        public int Delays { get; private set; }
    }

    private sealed class FakeProcess : ILocalServerProcess
    {
        public List<LocalServerLaunch> Launches { get; } = new();

        public Exception? StartFailure { get; set; }

        public bool IsFree { get; set; } = true;

        public bool IsAddressFree(string baseUrl) => IsFree;

        public void StartDetached(LocalServerLaunch launch)
        {
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            Launches.Add(launch);
        }
    }

    private sealed class FakeApi : ILocalServerApi
    {
        private readonly FakeProcess _process;
        private int _pollsSinceStart;

        public FakeApi(FakeProcess process)
        {
            _process = process;
        }

        public string? RunningVersion { get; set; }

        public int AnswersAfterStartPolls { get; set; } = 1;

        public bool AcceptsShutdown { get; set; } = true;

        public List<string> ShutdownTokens { get; } = new();

        public Task<string?> GetVersionAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            if (RunningVersion is null && _process.Launches.Count > 0 && ++_pollsSinceStart >= AnswersAfterStartPolls)
            {
                RunningVersion = APP_VERSION;
            }

            return Task.FromResult(RunningVersion);
        }

        public Task<bool> RequestShutdownAsync(string baseUrl, string controlToken, CancellationToken cancellationToken = default)
        {
            if (!AcceptsShutdown)
            {
                return Task.FromResult(false);
            }

            ShutdownTokens.Add(controlToken);
            RunningVersion = null;

            return Task.FromResult(true);
        }
    }

    private sealed class FakeState : ILocalServerStateStore
    {
        public string? ControlToken { get; set; }

        public string? ReadControlToken() => ControlToken;

        public void SaveControlToken(string controlToken) => ControlToken = controlToken;

        public void Clear() => ControlToken = null;
    }
}
