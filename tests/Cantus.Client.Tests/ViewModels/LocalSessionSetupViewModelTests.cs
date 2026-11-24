using System.Collections.Generic;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LocalSessionSetupViewModelTests
{
    private readonly Queue<LocalSessionStatus> _answers = new();
    private readonly List<string?> _sentCodes = new();
    private readonly LocalSessionSetupViewModel _setup;

    public LocalSessionSetupViewModelTests()
    {
        _setup = new LocalSessionSetupViewModel(code =>
        {
            _sentCodes.Add(code);
            LocalSessionStatus status = _answers.Count > 0 ? _answers.Dequeue() : LocalSessionStatus.Created;
            return Task.FromResult(new LocalSessionResult(status));
        });
    }

    [Fact]
    public async Task WithoutAJoinCode_ConnectsRightAway()
    {
        // Arrange
        _setup.Reset(joinCodeRequired: false, joinCodePrefill: "IGNORED1");

        // Act
        bool created = await _setup.ConnectAsync();

        // Assert
        created.Should().BeTrue();
        _sentCodes.Should().Equal(new string?[] { null });
        _setup.JoinCodeVisibility.Should().Be(Visibility.Collapsed);
        _setup.ErrorVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task WhenAJoinCodeIsRequired_ShowsTheFieldAndWaitsForACode()
    {
        // Arrange
        _setup.Reset(joinCodeRequired: true, joinCodePrefill: null);

        // Act
        bool created = await _setup.ConnectAsync();

        // Assert
        _setup.JoinCodeVisibility.Should().Be(Visibility.Visible);
        _setup.IntroText.Should().Be(LocalSessionSetupViewModel.JOIN_CODE_INTRO_TEXT);
        _setup.PrimaryButtonText.Should().Be("Connect");
        _setup.CanConnect.Should().BeFalse();
        created.Should().BeFalse();
        _sentCodes.Should().BeEmpty();
        _setup.ErrorText.Should().Be(LocalSessionSetupViewModel.JOIN_CODE_MISSING_TEXT);
    }

    [Fact]
    public async Task WhenAJoinCodeIsRequired_SendsTheTrimmedCode()
    {
        // Arrange
        _setup.Reset(joinCodeRequired: true, joinCodePrefill: null);

        // Act
        _setup.JoinCodeText = "  JOINCODE1 ";
        bool created = await _setup.ConnectAsync();

        // Assert
        _setup.CanConnect.Should().BeTrue();
        created.Should().BeTrue();
        _sentCodes.Should().Equal("JOINCODE1");
    }

    [Fact]
    public void Reset_PrefillsTheJoinCode()
    {
        // Act
        _setup.Reset(joinCodeRequired: true, joinCodePrefill: " FROMENV1 ");

        // Assert
        _setup.JoinCodeText.Should().Be("FROMENV1");
        _setup.CanConnect.Should().BeTrue();
    }

    [Fact]
    public async Task WhenTheServerAsksForAJoinCodeUnannounced_ShowsTheField()
    {
        // Arrange: the setup info said no join code, the server says otherwise.
        _setup.Reset(joinCodeRequired: false, joinCodePrefill: null);
        _answers.Enqueue(LocalSessionStatus.JoinCodeRequired);

        // Act
        bool created = await _setup.ConnectAsync();

        // Assert
        created.Should().BeFalse();
        _setup.IsJoinCodeVisible.Should().BeTrue();
        _setup.ErrorText.Should().Be(LocalSessionSetupViewModel.JOIN_CODE_MISSING_TEXT);
    }

    [Fact]
    public async Task WhenTheJoinCodeIsWrong_ShowsAnInlineErrorAndKeepsTheFieldForAnotherTry()
    {
        // Arrange
        _setup.Reset(joinCodeRequired: true, joinCodePrefill: null);
        _setup.JoinCodeText = "WRONGCODE";
        _answers.Enqueue(LocalSessionStatus.JoinCodeInvalid);

        // Act
        bool first = await _setup.ConnectAsync();

        // Assert
        first.Should().BeFalse();
        _setup.ErrorText.Should().Be(LocalSessionSetupViewModel.JOIN_CODE_INVALID_TEXT);
        _setup.ErrorVisibility.Should().Be(Visibility.Visible);
        _setup.JoinCodeVisibility.Should().Be(Visibility.Visible);
        _setup.CanConnect.Should().BeTrue();

        // Act
        _setup.JoinCodeText = "RIGHTCODE";
        bool second = await _setup.ConnectAsync();

        // Assert
        second.Should().BeTrue();
        _setup.ErrorVisibility.Should().Be(Visibility.Collapsed);
        _sentCodes.Should().Equal("WRONGCODE", "RIGHTCODE");
    }

    [Theory]
    [InlineData(LocalSessionStatus.Disabled, LocalSessionSetupViewModel.DISABLED_TEXT)]
    [InlineData(LocalSessionStatus.RateLimited, LocalSessionSetupViewModel.RATE_LIMITED_TEXT)]
    [InlineData(LocalSessionStatus.LimitReached, LocalSessionSetupViewModel.LIMIT_REACHED_TEXT)]
    [InlineData(LocalSessionStatus.InvalidDisplayName, LocalSessionSetupViewModel.INVALID_NAME_TEXT)]
    [InlineData(LocalSessionStatus.Failed, LocalSessionSetupViewModel.FAILED_TEXT)]
    public async Task OtherRefusals_ShowAOneLineMessageWithoutAJoinCodeField(LocalSessionStatus status, string message)
    {
        // Arrange
        _setup.Reset(joinCodeRequired: false, joinCodePrefill: null);
        _answers.Enqueue(status);

        // Act
        bool created = await _setup.ConnectAsync();

        // Assert
        created.Should().BeFalse();
        _setup.ErrorText.Should().Be(message);
        _setup.IsJoinCodeVisible.Should().BeFalse();
        _setup.PrimaryButtonText.Should().Be("Try again");
        _setup.CanConnect.Should().BeTrue();
    }

    [Fact]
    public async Task WhenCreationThrows_ReportsTheServerAsUnreachable()
    {
        // Arrange
        LocalSessionSetupViewModel setup = new(_ => throw new System.Net.Http.HttpRequestException("down"));
        setup.Reset(joinCodeRequired: false, joinCodePrefill: null);

        // Act
        bool created = await setup.ConnectAsync();

        // Assert
        created.Should().BeFalse();
        setup.ErrorText.Should().Be(LocalSessionSetupViewModel.FAILED_TEXT);
        setup.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Reset_WhileAnAttemptIsRunning_KeepsItBusy()
    {
        // Arrange
        TaskCompletionSource<LocalSessionResult> pending = new();
        int creations = 0;
        LocalSessionSetupViewModel setup = new(_ =>
        {
            creations++;
            return pending.Task;
        });
        setup.Reset(joinCodeRequired: false, joinCodePrefill: null);
        Task<bool> first = setup.ConnectAsync();

        // Act
        setup.Reset(joinCodeRequired: false, joinCodePrefill: null);
        bool second = await setup.ConnectAsync();

        // Assert
        second.Should().BeFalse();
        setup.IsBusy.Should().BeTrue();
        setup.CanConnect.Should().BeFalse();
        creations.Should().Be(1);

        // Act
        pending.SetResult(new LocalSessionResult(LocalSessionStatus.Created));

        // Assert
        (await first).Should().BeTrue();
        setup.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Reset_ClearsAnEarlierError()
    {
        // Arrange
        _setup.Reset(joinCodeRequired: false, joinCodePrefill: null);
        _answers.Enqueue(LocalSessionStatus.Failed);
        await _setup.ConnectAsync();

        // Act
        _setup.Reset(joinCodeRequired: false, joinCodePrefill: null);

        // Assert
        _setup.ErrorVisibility.Should().Be(Visibility.Collapsed);
        _setup.IntroText.Should().Be(LocalSessionSetupViewModel.INTRO_TEXT);
    }
}
