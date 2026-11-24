using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using Cantus.Core.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelDisplayManagementTests
{
    private const double PHONE_WIDTH = 400;
    private const double PHONE_HEIGHT = 800;
    private const double PHONE_HEIGHT_ROTATED = 820;

    [Fact]
    public void SigningIn_LoadsTheOwnersDisplays()
    {
        // Arrange
        ManagementFixture fixture = new();

        // Act
        fixture.SignIn();

        // Assert
        fixture.ViewModel.Session.Displays.IsSignedIn.Should().BeTrue();
        fixture.ViewModel.Session.Displays.Displays.Should().ContainSingle(d => d.Name == "TV");
        fixture.ListCalls().Should().Be(1);
    }

    [Fact]
    public void OpeningTheMobileSettingsTab_RefreshesTheDisplays()
    {
        // Arrange
        ManagementFixture fixture = new();
        fixture.SignIn();
        fixture.Layout.UpdateDimensions(PHONE_WIDTH, PHONE_HEIGHT);

        // Act
        fixture.Layout.MobileView = MobileViewMode.SyncAndSettings;

        // Assert
        fixture.ListCalls().Should().Be(2);

        // Act
        fixture.Layout.UpdateDimensions(PHONE_WIDTH, PHONE_HEIGHT_ROTATED);

        // Assert
        fixture.ListCalls().Should().Be(2, "settings stayed open");

        // Act
        fixture.Layout.MobileView = MobileViewMode.Lyrics;
        fixture.Layout.MobileView = MobileViewMode.SyncAndSettings;

        // Assert
        fixture.ListCalls().Should().Be(3);
    }

    [Fact]
    public void OpeningTheMobileSettingsTab_WhenSignedOut_DoesNotCallTheServer()
    {
        // Arrange
        ManagementFixture fixture = new();
        fixture.Layout.UpdateDimensions(PHONE_WIDTH, PHONE_HEIGHT);

        // Act
        fixture.Layout.MobileView = MobileViewMode.SyncAndSettings;

        // Assert
        fixture.ListCalls().Should().Be(0);
    }

    [Fact]
    public void DisplaysChanged_RefetchesTheDisplays()
    {
        // Arrange
        ManagementFixture fixture = new();
        fixture.SignIn();
        fixture.Api
            .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayListResult(
                DisplayManagementStatus.Succeeded,
                new List<DisplayInfoPayload>
                {
                    new() { Id = "display-1", Name = "TV" },
                    new() { Id = "display-2", Name = "Kitchen" },
                }));

        // Act
        fixture.Client.Raise(c => c.DisplaysChanged += null);

        // Assert
        fixture.ListCalls().Should().Be(2);
        fixture.ViewModel.Session.Displays.Displays.Select(d => d.Name).Should().Equal("TV", "Kitchen");
    }

    [Fact]
    public void DisplaysChanged_WhenSignedOut_DoesNotCallTheServer()
    {
        // Arrange
        ManagementFixture fixture = new();

        // Act
        fixture.Client.Raise(c => c.DisplaysChanged += null);

        // Assert
        fixture.ListCalls().Should().Be(0);
    }

    [Fact]
    public void SigningIn_ReopensTheApprovalForACodeKeptThroughSignIn()
    {
        // Arrange
        InMemorySettingsStore store = new();
        ManagementFixture before = new(store);
        before.ViewModel.Session.Displays.OpenApprovalFromLink("?pair=K7QH2MZP");
        ManagementFixture fixture = new(store);

        // Act
        fixture.SignIn();

        // Assert
        fixture.ViewModel.Session.Displays.PromptMode.Should().Be(DisplayPromptMode.Approve);
        fixture.ViewModel.Session.Displays.PromptCode.Should().Be("K7QH2MZP");
    }

    private sealed class ManagementFixture
    {
        public ManagementFixture(InMemorySettingsStore? pendingCodes = null)
        {
            Client = new Mock<ISignalRPlaybackClient>();
            Client.SetupProperty(c => c.DisplayToken);
            Client.SetupGet(c => c.ServerBaseUrl).Returns("http://localhost:5000");
            Client.Setup(c => c.SubscribeToUserAsync(It.IsAny<string?>())).Returns(Task.CompletedTask);

            Api = new Mock<IDisplayManagementApi>();
            Api
                .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DisplayListResult(
                    DisplayManagementStatus.Succeeded,
                    new List<DisplayInfoPayload> { new() { Id = "display-1", Name = "TV" } }));

            Layout = new ResponsiveLayoutManager();
            ViewModel = new LyricsViewModel(
                Client.Object,
                new ThemeManager(),
                Layout,
                new ClientPreferences(new InMemorySettingsStore()),
                new Mock<IDisplayPairingApi>().Object,
                new DisplayCredentialStore(new InMemorySettingsStore()),
                displaysApi: Api.Object,
                pendingPairCodes: new PendingPairCodeStore(pendingCodes ?? new InMemorySettingsStore()));
        }

        public Mock<ISignalRPlaybackClient> Client { get; }

        public Mock<IDisplayManagementApi> Api { get; }

        public ResponsiveLayoutManager Layout { get; }

        public LyricsViewModel ViewModel { get; }

        public void SignIn()
        {
            IReadOnlyList<AuthorizedSessionPayload> sessions = new List<AuthorizedSessionPayload>
            {
                new() { Id = "user-1", DisplayName = "Alice" },
            };
            Client.Raise(c => c.SessionsReceived += null, sessions);
        }

        public int ListCalls() => Api.Invocations.Count(i => i.Method.Name == nameof(IDisplayManagementApi.ListAsync));
    }
}
