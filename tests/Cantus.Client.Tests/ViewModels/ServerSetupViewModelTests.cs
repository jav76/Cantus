using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class ServerSetupViewModelTests
{
    private const string SERVER_URL = "http://192.168.0.10:5000";
    private const string OTHER_URL = "https://lyrics.example.com";

    [Fact]
    public void FirstRun_OpensWithAnEmptyAddressAndNoError()
    {
        Fixture fixture = new();

        fixture.Setup.AddressText.Should().BeEmpty();
        fixture.Setup.AddressErrorVisibility.Should().Be(Visibility.Collapsed);
        fixture.Setup.CanSave.Should().BeFalse();
        fixture.Setup.SaveButtonText.Should().Be(ServerSetupViewModel.SAVE_TEXT);
        fixture.Setup.ChangeWarningVisibility.Should().Be(Visibility.Collapsed);
    }

    [Theory]
    [InlineData("192.168.0.10:5000")]
    [InlineData("lyrics.example.com")]
    [InlineData("ftp://192.168.0.10")]
    public void InvalidAddress_ShowsAnErrorAndCannotBeSaved(string address)
    {
        Fixture fixture = new();

        fixture.Setup.AddressText = address;

        fixture.Setup.IsAddressValid.Should().BeFalse();
        fixture.Setup.AddressError.Should().Be(ServerSetupViewModel.INVALID_ADDRESS_TEXT);
        fixture.Setup.AddressErrorVisibility.Should().Be(Visibility.Visible);
        fixture.Setup.CanSave.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_InvalidAddress_SavesNothingAndDoesNotProbe()
    {
        Fixture fixture = new();
        fixture.Setup.AddressText = "192.168.0.10:5000";

        ServerSetupResult result = await fixture.Setup.SaveAsync();

        result.Should().Be(ServerSetupResult.NotSaved);
        fixture.Probe.ProbedUrls.Should().BeEmpty();
        fixture.Store.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_ReachableServer_SavesTheNormalizedAddress()
    {
        Fixture fixture = new();
        fixture.Setup.AddressText = $" {SERVER_URL}/ ";

        ServerSetupResult result = await fixture.Setup.SaveAsync();

        result.Should().Be(ServerSetupResult.Changed);
        fixture.Probe.ProbedUrls.Should().Equal(SERVER_URL);
        fixture.Settings.SavedServerUrl.Should().Be(SERVER_URL);
        fixture.Settings.IsSetupDone.Should().BeTrue();
        fixture.Setup.CheckState.Should().Be(ServerCheckState.Reachable);
    }

    [Fact]
    public async Task SaveAsync_UnreachableServer_WarnsFirstAndSavesOnTheSecondSave()
    {
        Fixture fixture = new();
        fixture.Probe.IsReachable = false;
        fixture.Setup.AddressText = SERVER_URL;

        ServerSetupResult first = await fixture.Setup.SaveAsync();

        first.Should().Be(ServerSetupResult.NotSaved);
        fixture.Settings.SavedServerUrl.Should().BeNull();
        fixture.Setup.CheckState.Should().Be(ServerCheckState.Unreachable);
        fixture.Setup.IsStatusWarning.Should().BeTrue();
        fixture.Setup.StatusText.Should().Contain(SERVER_URL);
        fixture.Setup.StatusVisibility.Should().Be(Visibility.Visible);
        fixture.Setup.SaveButtonText.Should().Be(ServerSetupViewModel.SAVE_ANYWAY_TEXT);
        fixture.Setup.CanSave.Should().BeTrue();

        ServerSetupResult second = await fixture.Setup.SaveAsync();

        second.Should().Be(ServerSetupResult.Changed);
        fixture.Settings.SavedServerUrl.Should().Be(SERVER_URL);
        fixture.Probe.ProbedUrls.Should().HaveCount(1);
    }

    [Fact]
    public async Task EditingTheAddress_AfterAWarning_ChecksTheNewAddressAgain()
    {
        Fixture fixture = new();
        fixture.Probe.IsReachable = false;
        fixture.Setup.AddressText = SERVER_URL;
        await fixture.Setup.SaveAsync();

        fixture.Setup.AddressText = OTHER_URL;

        fixture.Setup.CheckState.Should().Be(ServerCheckState.None);
        fixture.Setup.SaveButtonText.Should().Be(ServerSetupViewModel.SAVE_TEXT);
        (await fixture.Setup.SaveAsync()).Should().Be(ServerSetupResult.NotSaved);
        fixture.Probe.ProbedUrls.Should().Equal(SERVER_URL, OTHER_URL);
        fixture.Settings.SavedServerUrl.Should().BeNull();
    }

    [Fact]
    public void Skip_OnFirstRun_RemembersTheChoiceAndKeepsCredentials()
    {
        Fixture fixture = new();

        ServerSetupResult result = fixture.Setup.Skip();

        result.Should().Be(ServerSetupResult.Unchanged);
        fixture.Settings.SavedServerUrl.Should().BeNull();
        fixture.Settings.IsSetupDone.Should().BeTrue();
        fixture.ForgetCount.Should().Be(0);
        ServerSelection.Resolve(null, ServerUrlSource.Default, fixture.Settings).NeedsSetup.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_AnotherServer_ForgetsThisDevicesCredentials()
    {
        Fixture fixture = new(savedUrl: SERVER_URL);
        fixture.Setup.AddressText = OTHER_URL;

        fixture.Setup.ChangeWarningVisibility.Should().Be(Visibility.Visible);
        ServerSetupResult result = await fixture.Setup.SaveAsync();

        result.Should().Be(ServerSetupResult.Changed);
        fixture.Settings.SavedServerUrl.Should().Be(OTHER_URL);
        fixture.ForgetCount.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_TheServerAlreadyInUse_KeepsCredentials()
    {
        Fixture fixture = new(savedUrl: SERVER_URL);

        fixture.Setup.AddressText.Should().Be(SERVER_URL);
        fixture.Setup.ChangeWarningVisibility.Should().Be(Visibility.Collapsed);
        ServerSetupResult result = await fixture.Setup.SaveAsync();

        result.Should().Be(ServerSetupResult.Unchanged);
        fixture.ForgetCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveAsync_ThisComputerOnFirstRun_IsNotAChange()
    {
        Fixture fixture = new();
        fixture.Setup.UseThisComputer();

        ServerSetupResult result = await fixture.Setup.SaveAsync();

        result.Should().Be(ServerSetupResult.Unchanged);
        fixture.Settings.SavedServerUrl.Should().Be(ServerAddress.DEFAULT_BASE_URL);
        fixture.ForgetCount.Should().Be(0);
    }

    [Theory]
    [InlineData(ServerUrlSource.CommandLine, "--server-url")]
    [InlineData(ServerUrlSource.EnvironmentVariable, "CANTUS_SERVER_URL")]
    public async Task OverriddenServer_IsShownButCannotBeChanged(ServerUrlSource source, string expectedName)
    {
        Fixture fixture = new(savedUrl: SERVER_URL, overrideUrl: OTHER_URL, overrideSource: source);

        fixture.Setup.AddressText.Should().Be(OTHER_URL);
        fixture.Setup.IsLocked.Should().BeTrue();
        fixture.Setup.IsEditable.Should().BeFalse();
        fixture.Setup.LockedText.Should().Contain(expectedName);
        fixture.Setup.LockedVisibility.Should().Be(Visibility.Visible);
        fixture.Setup.CanSave.Should().BeFalse();

        (await fixture.Setup.SaveAsync()).Should().Be(ServerSetupResult.NotSaved);
        fixture.Setup.Skip().Should().Be(ServerSetupResult.NotSaved);
        fixture.Settings.SavedServerUrl.Should().Be(SERVER_URL);
        fixture.ForgetCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveAsync_WhileChecking_CannotBeStartedTwice()
    {
        Fixture fixture = new();
        TaskCompletionSource<bool> pending = new();
        fixture.Probe.Pending = pending;
        fixture.Setup.AddressText = SERVER_URL;

        Task<ServerSetupResult> first = fixture.Setup.SaveAsync();

        fixture.Setup.IsChecking.Should().BeTrue();
        fixture.Setup.CanSave.Should().BeFalse();
        (await fixture.Setup.SaveAsync()).Should().Be(ServerSetupResult.NotSaved);

        pending.SetResult(true);
        (await first).Should().Be(ServerSetupResult.Changed);
        fixture.Probe.ProbedUrls.Should().HaveCount(1);
    }

    [Fact]
    public void AddressText_Change_RaisesPropertyChanged()
    {
        Fixture fixture = new();
        int raised = 0;
        fixture.Setup.PropertyChanged += (_, _) => raised++;

        fixture.Setup.AddressText = SERVER_URL;
        fixture.Setup.AddressText = SERVER_URL;

        raised.Should().Be(1);
    }

    [Fact]
    public void FirstRun_WithoutABundledServer_OffersToSkip()
    {
        Fixture fixture = new();

        fixture.Setup.HasLocalServer.Should().BeFalse();
        fixture.Setup.SkipButtonText.Should().Be(ServerSetupViewModel.SKIP_TEXT);
        fixture.Setup.SkipHintText.Should().StartWith("Skip if the server runs on this computer.");
        fixture.Setup.LocalServerVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void FirstRun_WithABundledServer_OffersToRunItHere()
    {
        Fixture fixture = new(hasLocalServer: true);

        fixture.Setup.HasLocalServer.Should().BeTrue();
        fixture.Setup.SkipButtonText.Should().Be(ServerSetupViewModel.RUN_HERE_TEXT);
        fixture.Setup.SkipHintText.Should().Contain(ServerAddress.DEFAULT_BASE_URL);
        fixture.Setup.LocalServerVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task SaveAsync_ThisComputerWithABundledServer_StartsItBeforeChecking()
    {
        Fixture fixture = new(savedUrl: SERVER_URL, hasLocalServer: true);
        fixture.Probe.IsReachable = false;
        fixture.LocalServer.OnEnsureRunning = () => fixture.Probe.IsReachable = true;
        fixture.Setup.UseThisComputer();

        ServerSetupResult result = await fixture.Setup.SaveAsync();

        result.Should().Be(ServerSetupResult.Changed);
        fixture.LocalServer.EnsuredUrls.Should().Equal(ServerAddress.DEFAULT_BASE_URL);
        fixture.Settings.SavedServerUrl.Should().Be(ServerAddress.DEFAULT_BASE_URL);
    }

    [Fact]
    public async Task SaveAsync_AnotherServer_DoesNotStartTheBundledOne()
    {
        Fixture fixture = new(hasLocalServer: true);
        fixture.Setup.AddressText = SERVER_URL;

        await fixture.Setup.SaveAsync();

        fixture.LocalServer.EnsuredUrls.Should().BeEmpty();
    }

    [Fact]
    public void ServerSetting_OnTheBundledServer_ShowsThatItKeepsRunning()
    {
        Fixture fixture = new(savedUrl: ServerAddress.DEFAULT_BASE_URL, hasLocalServer: true);

        fixture.Setup.LocalServerVisibility.Should().Be(Visibility.Visible);
        fixture.Setup.LocalServerText.Should().Be(ServerSetupViewModel.LOCAL_SERVER_RUNNING_TEXT);
        fixture.Setup.CanStopLocalServer.Should().BeTrue();
    }

    [Fact]
    public void ServerSetting_OnAnotherServer_HidesTheBundledServer()
    {
        Fixture fixture = new(savedUrl: SERVER_URL, hasLocalServer: true);

        fixture.Setup.LocalServerVisibility.Should().Be(Visibility.Collapsed);
    }

    [Theory]
    [InlineData(true, ServerSetupViewModel.LOCAL_SERVER_STOPPED_TEXT)]
    [InlineData(false, ServerSetupViewModel.LOCAL_SERVER_NOT_STOPPED_TEXT)]
    public async Task StopLocalServerAsync_SaysWhetherTheServerStopped(bool canStop, string expectedText)
    {
        Fixture fixture = new(savedUrl: ServerAddress.DEFAULT_BASE_URL, hasLocalServer: true);
        fixture.LocalServer.CanStop = canStop;

        await fixture.Setup.StopLocalServerAsync();

        fixture.LocalServer.StoppedUrls.Should().Equal(ServerAddress.DEFAULT_BASE_URL);
        fixture.Setup.LocalServerText.Should().Be(expectedText);
        fixture.Setup.CanStopLocalServer.Should().BeTrue();
    }

    private sealed class Fixture
    {
        public Fixture(
            string? savedUrl = null,
            string? overrideUrl = null,
            ServerUrlSource overrideSource = ServerUrlSource.Default,
            bool hasLocalServer = false)
        {
            Settings = new ServerSettingsStore(Store);
            if (savedUrl is not null)
            {
                Settings.Save(savedUrl);
            }

            ServerSelection current = ServerSelection.Resolve(overrideUrl, overrideSource, Settings);
            Setup = new ServerSetupViewModel(
                Settings,
                current,
                Probe,
                () => ForgetCount++,
                hasLocalServer ? LocalServer : null);
        }

        public InMemorySettingsStore Store { get; } = new();

        public ServerSettingsStore Settings { get; }

        public FakeProbe Probe { get; } = new();

        public FakeLocalServer LocalServer { get; } = new();

        public ServerSetupViewModel Setup { get; }

        public int ForgetCount { get; private set; }
    }

    private sealed class FakeLocalServer : ILocalServer
    {
        public bool CanStop { get; set; } = true;

        public Action? OnEnsureRunning { get; set; }

        public List<string> EnsuredUrls { get; } = new();

        public List<string> StoppedUrls { get; } = new();

        public bool Manages(string baseUrl) => ServerAddress.AreSame(baseUrl, ServerAddress.DEFAULT_BASE_URL);

        public Task<LocalServerStartResult> EnsureRunningAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            EnsuredUrls.Add(baseUrl);
            OnEnsureRunning?.Invoke();

            return Task.FromResult(LocalServerStartResult.Started);
        }

        public Task<bool> StopAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            StoppedUrls.Add(baseUrl);

            return Task.FromResult(CanStop);
        }
    }

    private sealed class FakeProbe : IServerProbe
    {
        public bool IsReachable { get; set; } = true;

        public TaskCompletionSource<bool>? Pending { get; set; }

        public List<string> ProbedUrls { get; } = new();

        public Task<bool> IsReachableAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            ProbedUrls.Add(baseUrl);

            return Pending?.Task ?? Task.FromResult(IsReachable);
        }
    }
}
