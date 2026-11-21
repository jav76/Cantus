using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class DisplaysViewModelTests
{
    private static readonly DateTimeOffset NOW = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("?pair=K7QH2MZP", "K7QH2MZP")]
    [InlineData("http://192.168.0.10:5000/?pair=K7QH2MZP", "K7QH2MZP")]
    [InlineData("?pair=k7qh-2mzp", "K7QH2MZP")]
    [InlineData("?pair=K7QH%202MZP", "K7QH2MZP")]
    [InlineData("?pair=K7QH+2MZP", "K7QH2MZP")]
    [InlineData("?theme=dark&pair=K7QH2MZP&x=1", "K7QH2MZP")]
    [InlineData("http://host/?pair=K7QH2MZP#top", "K7QH2MZP")]
    [InlineData("pair=K7QH2MZP", "K7QH2MZP")]
    public void ParsePairCode_ReadsAndNormalizesThePairParameter(string link, string expected)
    {
        // Act
        string? code = DisplaysViewModel.ParsePairCode(link);

        // Assert
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://192.168.0.10:5000/")]
    [InlineData("?auth=session-1")]
    [InlineData("?pair=")]
    [InlineData("?pair")]
    [InlineData("?pairing=K7QH2MZP")]
    [InlineData("?PAIR=K7QH2MZP")]
    [InlineData("?pair=%3Cscript%3E")]
    [InlineData("?pair=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("http://host/#?pair=K7QH2MZP")]
    public void ParsePairCode_WithoutAUsableCode_ReturnsNull(string? link)
    {
        // Act
        string? code = DisplaysViewModel.ParsePairCode(link);

        // Assert
        code.Should().BeNull();
    }

    [Fact]
    public void OpenApprovalFromLink_OpensThePromptWithTheCodeFilledIn()
    {
        // Arrange
        DisplaysFixture fixture = new();

        // Act
        bool opened = fixture.Displays.OpenApprovalFromLink("?pair=K7QH-2MZP");

        // Assert
        opened.Should().BeTrue();
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Approve);
        fixture.Displays.PromptVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.PromptTitle.Should().Be(DisplaysViewModel.APPROVE_TITLE);
        fixture.Displays.PromptCode.Should().Be("K7QH2MZP");
        fixture.Displays.PromptName.Should().BeEmpty();
        fixture.Displays.CodeFieldVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.NameFieldVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.ConfirmText.Should().Be("Approve");
        fixture.Displays.CanConfirm.Should().BeFalse("the Display still needs a name");
    }

    [Fact]
    public void OpenApprovalFromLink_WithoutACode_LeavesThePromptClosed()
    {
        // Arrange
        DisplaysFixture fixture = new();

        // Act
        bool opened = fixture.Displays.OpenApprovalFromLink("?theme=dark");

        // Assert
        opened.Should().BeFalse();
        fixture.Displays.IsPromptOpen.Should().BeFalse();
        fixture.Displays.PromptVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ApprovalPrompt_WhenSignedOut_ShowsTheSignInHint()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);

        // Act
        fixture.Displays.OpenApproval();

        // Assert
        fixture.Displays.SignInHintVisibility.Should().Be(Visibility.Visible);

        // Act
        fixture.Displays.SetSignedIn(true);

        // Assert
        fixture.Displays.SignInHintVisibility.Should().Be(Visibility.Collapsed);
    }

    [Theory]
    [InlineData("", "TV", false)]
    [InlineData("K7QH2MZP", "", false)]
    [InlineData("K7QH2MZP", "   ", false)]
    [InlineData("K7QH 2MZP", "TV", true)]
    [InlineData("K7QH?2MZP", "TV", false)]
    [InlineData("K7QH2MZP", "  Living room TV  ", true)]
    public void ApprovalPrompt_EnablesApproveOnlyForAUsableCodeAndName(string code, string name, bool expected)
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.Displays.OpenApproval();

        // Act
        fixture.Displays.PromptCode = code;
        fixture.Displays.PromptName = name;

        // Assert
        fixture.Displays.CanConfirm.Should().Be(expected);
    }

    [Fact]
    public void ApprovalPrompt_RejectsANameOverTheServerLimit()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.Displays.OpenApproval("K7QH2MZP");

        // Act
        fixture.Displays.PromptName = new string('a', DisplaysViewModel.MAX_NAME_LENGTH + 1);

        // Assert
        fixture.Displays.CanConfirm.Should().BeFalse();

        // Act
        fixture.Displays.PromptName = new string('a', DisplaysViewModel.MAX_NAME_LENGTH);

        // Assert
        fixture.Displays.CanConfirm.Should().BeTrue();
    }

    [Fact]
    public async Task Approve_WithAValidCode_SendsItClosesThePromptAndRefreshesTheList()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ApprovalReturns(DisplayManagementStatus.Succeeded, Display("display-1", "Living room TV"));
        fixture.ListReturns(Display("display-1", "Living room TV", NOW.AddSeconds(-5)));
        fixture.Displays.OpenApprovalFromLink("?pair=k7qh-2mzp");
        fixture.Displays.PromptName = "  Living room TV ";

        // Act
        bool approved = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        approved.Should().BeTrue();
        fixture.Api.Verify(a => a.ApproveAsync("K7QH2MZP", "Living room TV", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Displays.IsPromptOpen.Should().BeFalse();
        fixture.Displays.PromptCode.Should().BeEmpty();
        fixture.Displays.NoticeMessage.Should().Contain("Living room TV is paired");
        fixture.Displays.NoticeVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.Displays.Should().ContainSingle();
        fixture.Displays.Displays[0].Name.Should().Be("Living room TV");
        fixture.Displays.Displays[0].LastSeenText.Should().Be(DisplayItemViewModel.JUST_NOW_TEXT);
        fixture.Displays.EmptyVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task Approve_WithAnInvalidOrExpiredCode_KeepsThePromptOpenWithTheError()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ApprovalReturns(DisplayManagementStatus.NotFound);
        fixture.Displays.OpenApproval("K7QH2MZP");
        fixture.Displays.PromptName = "TV";

        // Act
        bool approved = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        approved.Should().BeFalse();
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Approve);
        fixture.Displays.PromptError.Should().Be(DisplaysViewModel.CODE_NOT_FOUND_MESSAGE);
        fixture.Displays.PromptErrorVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.PromptCode.Should().Be("K7QH2MZP");
        fixture.Displays.PromptName.Should().Be("TV");
        fixture.Displays.CanConfirm.Should().BeTrue("the owner can fix the code and try again");
        fixture.Displays.IsBusy.Should().BeFalse();
        fixture.Api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(DisplayManagementStatus.Failed, DisplaysViewModel.FAILED_MESSAGE)]
    [InlineData(DisplayManagementStatus.NotSignedIn, DisplaysViewModel.NOT_SIGNED_IN_MESSAGE)]
    [InlineData(DisplayManagementStatus.Forbidden, DisplaysViewModel.FORBIDDEN_MESSAGE)]
    [InlineData(DisplayManagementStatus.RateLimited, DisplaysViewModel.RATE_LIMITED_MESSAGE)]
    [InlineData(DisplayManagementStatus.InvalidName, DisplaysViewModel.INVALID_NAME_MESSAGE)]
    public async Task Approve_WhenRefusedOrUnreachable_ShowsWhy(DisplayManagementStatus status, string expected)
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ApprovalReturns(status);
        fixture.Displays.OpenApproval("K7QH2MZP");
        fixture.Displays.PromptName = "TV";

        // Act
        bool approved = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        approved.Should().BeFalse();
        fixture.Displays.IsPromptOpen.Should().BeTrue();
        fixture.Displays.PromptError.Should().Be(expected);
    }

    [Fact]
    public async Task Approve_RetriedAfterAnError_ClearsTheError()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ApprovalReturns(DisplayManagementStatus.Failed);
        fixture.Displays.OpenApproval("K7QH2MZP");
        fixture.Displays.PromptName = "TV";
        await fixture.Displays.ConfirmPromptAsync();
        fixture.ApprovalReturns(DisplayManagementStatus.Succeeded, Display("display-1", "TV"));

        // Act
        bool approved = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        approved.Should().BeTrue();
        fixture.Displays.PromptError.Should().BeEmpty();
        fixture.Displays.PromptErrorVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task Approve_WhileARequestIsRunning_IsNotSentTwice()
    {
        // Arrange
        DisplaysFixture fixture = new();
        TaskCompletionSource<DisplayApprovalResult> pending = new();
        fixture.Api
            .Setup(a => a.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        fixture.Displays.OpenApproval("K7QH2MZP");
        fixture.Displays.PromptName = "TV";

        // Act
        Task<bool> first = fixture.Displays.ConfirmPromptAsync();
        bool second = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        second.Should().BeFalse();
        fixture.Displays.IsBusy.Should().BeTrue();
        fixture.Displays.BusyVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.CanConfirm.Should().BeFalse();

        // Act
        pending.SetResult(new DisplayApprovalResult(DisplayManagementStatus.Succeeded, Display("display-1", "TV")));
        bool approved = await first;

        // Assert
        approved.Should().BeTrue();
        fixture.Displays.IsBusy.Should().BeFalse();
        fixture.Api.Verify(
            a => a.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Approve_CancelledWhileRunning_StillRefreshesWithoutTouchingANewPrompt()
    {
        // Arrange
        DisplaysFixture fixture = new();
        TaskCompletionSource<DisplayApprovalResult> pending = new();
        fixture.Api
            .Setup(a => a.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        fixture.ListReturns(Display("display-1", "TV"));
        fixture.Displays.OpenApproval("K7QH2MZP");
        fixture.Displays.PromptName = "TV";
        Task<bool> approval = fixture.Displays.ConfirmPromptAsync();

        // Act
        fixture.Displays.CancelPrompt();
        fixture.Displays.OpenApproval("AAAABBBB");
        pending.SetResult(new DisplayApprovalResult(DisplayManagementStatus.Succeeded, Display("display-1", "TV")));
        await approval;

        // Assert
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Approve);
        fixture.Displays.PromptCode.Should().Be("AAAABBBB");
        fixture.Displays.Displays.Should().ContainSingle(d => d.Id == "display-1");
    }

    [Fact]
    public async Task Refresh_ShowsEachDisplayWithItsLastSeenTime()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(
            Display("display-1", "Living room TV", NOW.AddMinutes(-5)),
            Display("display-2", "Kitchen", null));

        // Act
        await fixture.Displays.RefreshAsync();

        // Assert
        fixture.Displays.Displays.Select(d => d.Name).Should().Equal("Living room TV", "Kitchen");
        fixture.Displays.Displays[0].LastSeenText.Should().Be("Last seen 5 min ago");
        fixture.Displays.Displays[1].LastSeenText.Should().Be(DisplayItemViewModel.NEVER_SEEN_TEXT);
        fixture.Displays.EmptyVisibility.Should().Be(Visibility.Collapsed);
        fixture.Displays.ListErrorVisibility.Should().Be(Visibility.Collapsed);
        fixture.Displays.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_UsesTheServerClockForLastSeen()
    {
        // Arrange
        long offsetMs = (long)TimeSpan.FromHours(2).TotalMilliseconds;
        DisplaysFixture fixture = new(serverClockOffsetMs: offsetMs);
        fixture.ListReturns(Display("display-1", "TV", NOW.AddHours(2).AddSeconds(-10)));

        // Act
        await fixture.Displays.RefreshAsync();

        // Assert
        fixture.Displays.Displays[0].LastSeenText.Should().Be(DisplayItemViewModel.JUST_NOW_TEXT);
    }

    [Fact]
    public async Task Refresh_KeepsRowsForDisplaysStillListedAndDropsTheRest()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"), Display("display-2", "Kitchen"));
        await fixture.Displays.RefreshAsync();
        DisplayItemViewModel tv = fixture.Displays.Displays[0];
        fixture.ListReturns(Display("display-3", "Bedroom"), Display("display-1", "Den"));

        // Act
        await fixture.Displays.RefreshAsync();

        // Assert
        fixture.Displays.Displays.Select(d => d.Id).Should().Equal("display-3", "display-1");
        fixture.Displays.Displays[1].Should().BeSameAs(tv);
        tv.Name.Should().Be("Den");
    }

    [Fact]
    public async Task Refresh_WithNoDisplays_ShowsTheEmptyMessage()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns();

        // Assert
        fixture.Displays.EmptyVisibility.Should().Be(Visibility.Collapsed, "nothing has loaded yet");

        // Act
        await fixture.Displays.RefreshAsync();

        // Assert
        fixture.Displays.Displays.Should().BeEmpty();
        fixture.Displays.EmptyVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task Refresh_WhenTheServerIsUnreachable_KeepsTheListAndShowsAnError()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"));
        await fixture.Displays.RefreshAsync();
        fixture.Api
            .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayListResult(DisplayManagementStatus.Failed));

        // Act
        await fixture.Displays.RefreshAsync();

        // Assert
        fixture.Displays.Displays.Should().ContainSingle();
        fixture.Displays.ListError.Should().Be(DisplaysViewModel.LIST_FAILED_MESSAGE);
        fixture.Displays.ListErrorVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.EmptyVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task Refresh_WhenSignedOut_DoesNotCallTheServer()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);

        // Act
        await fixture.Displays.RefreshAsync();

        // Assert
        fixture.Api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Displays.SectionVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task Refresh_OverlappingRequests_KeepOnlyTheLatestAnswer()
    {
        // Arrange
        DisplaysFixture fixture = new();
        TaskCompletionSource<DisplayListResult> older = new();
        TaskCompletionSource<DisplayListResult> newer = new();
        fixture.Api
            .SetupSequence(a => a.ListAsync(It.IsAny<CancellationToken>()))
            .Returns(older.Task)
            .Returns(newer.Task);
        Task first = fixture.Displays.RefreshAsync();
        Task second = fixture.Displays.RefreshAsync();

        // Act
        newer.SetResult(new DisplayListResult(DisplayManagementStatus.Succeeded, new[] { Display("display-2", "New") }));
        await second;
        older.SetResult(new DisplayListResult(DisplayManagementStatus.Succeeded, new[] { Display("display-1", "Old") }));
        await first;

        // Assert
        fixture.Displays.Displays.Should().ContainSingle(d => d.Id == "display-2");
        fixture.Displays.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_SendsTheTrimmedNameUpdatesTheRowAndRefreshes()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"));
        await fixture.Displays.RefreshAsync();
        DisplayItemViewModel tv = fixture.Displays.Displays[0];
        fixture.Api
            .Setup(a => a.RenameAsync("display-1", "Bedroom", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DisplayManagementStatus.Succeeded);
        fixture.ListReturns(Display("display-1", "Bedroom"));

        // Act
        fixture.Displays.StartRename(tv);

        // Assert
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Rename);
        fixture.Displays.PromptTitle.Should().Be(DisplaysViewModel.RENAME_TITLE);
        fixture.Displays.PromptName.Should().Be("TV");
        fixture.Displays.CodeFieldVisibility.Should().Be(Visibility.Collapsed);
        fixture.Displays.NameFieldVisibility.Should().Be(Visibility.Visible);
        fixture.Displays.ConfirmText.Should().Be("Save");

        // Act
        fixture.Displays.PromptName = " Bedroom ";
        bool renamed = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        renamed.Should().BeTrue();
        fixture.Api.Verify(a => a.RenameAsync("display-1", "Bedroom", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        tv.Name.Should().Be("Bedroom");
        fixture.Displays.IsPromptOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_WhenItFails_KeepsTheOldNameAndShowsTheError()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"));
        await fixture.Displays.RefreshAsync();
        DisplayItemViewModel tv = fixture.Displays.Displays[0];
        fixture.Api
            .Setup(a => a.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DisplayManagementStatus.Failed);
        fixture.Displays.StartRename(tv);
        fixture.Displays.PromptName = "Bedroom";

        // Act
        bool renamed = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        renamed.Should().BeFalse();
        tv.Name.Should().Be("TV");
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Rename);
        fixture.Displays.PromptError.Should().Be(DisplaysViewModel.FAILED_MESSAGE);
    }

    [Fact]
    public async Task Rename_OfADisplayRemovedElsewhere_DropsItsRow()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"));
        await fixture.Displays.RefreshAsync();
        fixture.Api
            .Setup(a => a.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DisplayManagementStatus.NotFound);
        fixture.ListReturns();
        fixture.Displays.StartRename(fixture.Displays.Displays[0]);
        fixture.Displays.PromptName = "Bedroom";

        // Act
        bool renamed = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        renamed.Should().BeFalse();
        fixture.Displays.Displays.Should().BeEmpty();
        fixture.Displays.IsPromptOpen.Should().BeFalse();
        fixture.Displays.NoticeMessage.Should().Be(DisplaysViewModel.DISPLAY_GONE_MESSAGE);
    }

    [Fact]
    public async Task Revoke_AsksFirstThenRemovesTheRowAndRefreshes()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"), Display("display-2", "Kitchen"));
        await fixture.Displays.RefreshAsync();
        fixture.Api
            .Setup(a => a.RevokeAsync("display-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DisplayManagementStatus.Succeeded);
        fixture.ListReturns(Display("display-2", "Kitchen"));

        // Act
        fixture.Displays.StartRevoke(fixture.Displays.Displays[0]);

        // Assert
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Revoke);
        fixture.Displays.PromptMessage.Should().Contain("Remove TV?");
        fixture.Displays.NameFieldVisibility.Should().Be(Visibility.Collapsed);
        fixture.Displays.ConfirmText.Should().Be("Remove");
        fixture.Displays.CanConfirm.Should().BeTrue();
        fixture.Api.Verify(a => a.RevokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Act
        bool revoked = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        revoked.Should().BeTrue();
        fixture.Api.Verify(a => a.RevokeAsync("display-1", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Displays.Displays.Select(d => d.Id).Should().Equal("display-2");
        fixture.Displays.NoticeMessage.Should().Be("TV was removed.");
        fixture.Displays.IsPromptOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_WhenTheServerIsUnreachable_KeepsTheRowAndShowsTheError()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"));
        await fixture.Displays.RefreshAsync();
        fixture.Api
            .Setup(a => a.RevokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DisplayManagementStatus.Failed);
        fixture.Displays.StartRevoke(fixture.Displays.Displays[0]);

        // Act
        bool revoked = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        revoked.Should().BeFalse();
        fixture.Displays.Displays.Should().ContainSingle();
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Revoke);
        fixture.Displays.PromptError.Should().Be(DisplaysViewModel.FAILED_MESSAGE);
        fixture.Api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_OfADisplayAlreadyRemoved_DropsTheRow()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"));
        await fixture.Displays.RefreshAsync();
        fixture.Api
            .Setup(a => a.RevokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DisplayManagementStatus.NotFound);
        fixture.ListReturns();
        fixture.Displays.StartRevoke(fixture.Displays.Displays[0]);

        // Act
        bool revoked = await fixture.Displays.ConfirmPromptAsync();

        // Assert
        revoked.Should().BeFalse();
        fixture.Displays.Displays.Should().BeEmpty();
        fixture.Displays.NoticeMessage.Should().Be(DisplaysViewModel.DISPLAY_GONE_MESSAGE);
    }

    [Fact]
    public async Task SignOut_EmptiesTheListAndClosesARenamePrompt()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV"));
        await fixture.Displays.RefreshAsync();
        fixture.Displays.StartRename(fixture.Displays.Displays[0]);

        // Act
        fixture.Displays.SetSignedIn(false);

        // Assert
        fixture.Displays.Displays.Should().BeEmpty();
        fixture.Displays.IsPromptOpen.Should().BeFalse();
        fixture.Displays.SectionVisibility.Should().Be(Visibility.Collapsed);
        fixture.Displays.EmptyVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void SignOut_KeepsAnOpenApprovalPrompt()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.Displays.OpenApproval("K7QH2MZP");

        // Act
        fixture.Displays.SetSignedIn(false);

        // Assert
        fixture.Displays.PromptMode.Should().Be(DisplayPromptMode.Approve);
        fixture.Displays.PromptCode.Should().Be("K7QH2MZP");
    }

    [Fact]
    public async Task SignOut_DuringARefresh_IgnoresItsAnswer()
    {
        // Arrange
        DisplaysFixture fixture = new();
        TaskCompletionSource<DisplayListResult> pending = new();
        fixture.Api.Setup(a => a.ListAsync(It.IsAny<CancellationToken>())).Returns(pending.Task);
        Task refresh = fixture.Displays.RefreshAsync();

        // Act
        fixture.Displays.SetSignedIn(false);
        pending.SetResult(new DisplayListResult(DisplayManagementStatus.Succeeded, new[] { Display("display-1", "TV") }));
        await refresh;

        // Assert
        fixture.Displays.Displays.Should().BeEmpty();
        fixture.Displays.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task OpeningAPrompt_ClearsTheLastNotice()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ApprovalReturns(DisplayManagementStatus.Succeeded, Display("display-1", "TV"));
        fixture.Displays.OpenApproval("K7QH2MZP");
        fixture.Displays.PromptName = "TV";
        await fixture.Displays.ConfirmPromptAsync();

        // Act
        fixture.Displays.OpenApproval();

        // Assert
        fixture.Displays.NoticeVisibility.Should().Be(Visibility.Collapsed);
        fixture.Displays.PromptCode.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-30, DisplayItemViewModel.JUST_NOW_TEXT)]
    [InlineData(30, DisplayItemViewModel.JUST_NOW_TEXT)]
    [InlineData(60, "Last seen 1 min ago")]
    [InlineData(59 * 60, "Last seen 59 min ago")]
    [InlineData(3 * 60 * 60, "Last seen 3 h ago")]
    [InlineData(26 * 60 * 60, "Last seen 1 day ago")]
    [InlineData(6 * 24 * 60 * 60, "Last seen 6 days ago")]
    public void FormatLastSeen_DescribesHowLongAgo(int secondsAgo, string expected)
    {
        // Act
        string text = DisplayItemViewModel.FormatLastSeen(NOW.AddSeconds(-secondsAgo), NOW);

        // Assert
        text.Should().Be(expected);
    }

    [Fact]
    public void FormatLastSeen_AfterAWeek_ShowsTheDate()
    {
        // Arrange
        DateTimeOffset lastSeen = NOW.AddDays(-30);
        string expectedDate = lastSeen.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

        // Act
        string text = DisplayItemViewModel.FormatLastSeen(lastSeen, NOW);

        // Assert
        text.Should().Be($"Last seen {expectedDate}");
    }

    [Fact]
    public async Task LastSeenText_AgesWhileTheListIsShown()
    {
        // Arrange
        DisplaysFixture fixture = new();
        fixture.ListReturns(Display("display-1", "TV", NOW), Display("display-2", "Kitchen", null));
        await fixture.Displays.RefreshAsync();
        List<string?> raised = new();
        fixture.Displays.Displays[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        // Assert
        fixture.Displays.Displays[0].LastSeenText.Should().Be("Last seen 5 min ago");
        fixture.Displays.Displays[1].LastSeenText.Should().Be(DisplayItemViewModel.NEVER_SEEN_TEXT);
        raised.Should().Contain(nameof(DisplayItemViewModel.LastSeenText));
        fixture.Api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Once, "aging needs no fetch");
    }

    [Fact]
    public async Task LastSeenText_IsUpdatedOnTheUiThread()
    {
        // Arrange
        DisplaysFixture fixture = new(queueUiWork: true);
        fixture.ListReturns(Display("display-1", "TV", NOW));
        await fixture.Displays.RefreshAsync();

        // Act
        fixture.Time.Advance(TimeSpan.FromMinutes(2));

        // Assert
        fixture.Displays.Displays[0].LastSeenText.Should().Be(DisplayItemViewModel.JUST_NOW_TEXT);

        // Act
        fixture.RunUiQueue();

        // Assert
        fixture.Displays.Displays[0].LastSeenText.Should().Be("Last seen 2 min ago");
    }

    [Fact]
    public async Task LastSeenText_StopsAgingAfterSignOut()
    {
        // Arrange
        DisplaysFixture fixture = new(queueUiWork: true);
        await fixture.Displays.RefreshAsync();
        fixture.Displays.SetSignedIn(false);

        // Act
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        // Assert
        fixture.UiQueue.Should().BeEmpty();
    }

    [Fact]
    public void PairLink_OnASignedOutDevice_ReopensThePromptAfterSignIn()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);
        fixture.Displays.OpenApprovalFromLink("?pair=K7QH-2MZP");
        fixture.Displays.KeepCodeThroughSignIn();
        fixture.Time.Advance(TimeSpan.FromMinutes(2));

        // Act: the sign-in leaves the page and comes back without the link
        DisplaysFixture reloaded = fixture.Reload(signedIn: false);
        reloaded.Displays.IsPromptOpen.Should().BeFalse();
        reloaded.Displays.SetSignedIn(true);

        // Assert
        reloaded.Displays.PromptMode.Should().Be(DisplayPromptMode.Approve);
        reloaded.Displays.PromptCode.Should().Be("K7QH2MZP");
        reloaded.Displays.SignInHintVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void KeepCodeThroughSignIn_KeepsATypedCodeNormalized()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);
        fixture.Displays.OpenApproval();
        fixture.Displays.PromptCode = "k7qh-2mzp";

        // Act
        fixture.Displays.KeepCodeThroughSignIn();
        DisplaysFixture reloaded = fixture.Reload(signedIn: true);

        // Assert
        reloaded.Displays.PromptCode.Should().Be("K7QH2MZP");
    }

    [Fact]
    public void KeptCode_IsUsedOnlyOnce()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);
        fixture.Displays.OpenApprovalFromLink("?pair=K7QH2MZP");
        fixture.Reload(signedIn: true);

        // Act
        DisplaysFixture again = fixture.Reload(signedIn: true);

        // Assert
        again.Displays.IsPromptOpen.Should().BeFalse();
    }

    [Fact]
    public void KeptCode_AfterTheCodeExpired_IsDropped()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);
        fixture.Displays.OpenApprovalFromLink("?pair=K7QH2MZP");
        fixture.Time.Advance(PendingPairCodeStore.CODE_LIFETIME);

        // Act
        DisplaysFixture reloaded = fixture.Reload(signedIn: true);

        // Assert
        reloaded.Displays.IsPromptOpen.Should().BeFalse();
        reloaded.Store.GetValue(PendingPairCodeStore.CODE_KEY).Should().Be(string.Empty);
    }

    [Fact]
    public void CancellingTheApproval_ForgetsTheKeptCode()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);
        fixture.Displays.OpenApprovalFromLink("?pair=K7QH2MZP");

        // Act
        fixture.Displays.CancelPrompt();
        DisplaysFixture reloaded = fixture.Reload(signedIn: true);

        // Assert
        reloaded.Displays.IsPromptOpen.Should().BeFalse();
    }

    [Fact]
    public void PairLink_OnASignedInDevice_KeepsNothing()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: true);

        // Act
        fixture.Displays.OpenApprovalFromLink("?pair=K7QH2MZP");
        fixture.Displays.KeepCodeThroughSignIn();

        // Assert
        fixture.Store.GetValue(PendingPairCodeStore.CODE_KEY).Should().BeNull();
    }

    [Fact]
    public void SigningIn_WithAnApprovalPromptOpen_KeepsItsCode()
    {
        // Arrange
        DisplaysFixture fixture = new(signedIn: false);
        fixture.Displays.OpenApprovalFromLink("?pair=K7QH2MZP");
        fixture.Displays.PromptName = "TV";

        // Act
        fixture.Displays.SetSignedIn(true);

        // Assert
        fixture.Displays.PromptCode.Should().Be("K7QH2MZP");
        fixture.Displays.PromptName.Should().Be("TV", "the open prompt is not replaced");
        fixture.Displays.CanConfirm.Should().BeTrue();
    }

    private static DisplayInfoPayload Display(string id, string name, DateTimeOffset? lastSeen = null) => new()
    {
        Id = id,
        Name = name,
        CreatedAtUtc = NOW.AddDays(-1),
        LastSeenAtUtc = lastSeen,
    };

    private sealed class DisplaysFixture
    {
        private readonly long _serverClockOffsetMs;

        public DisplaysFixture(
            bool signedIn = true,
            long serverClockOffsetMs = 0,
            FakeTimeProvider? time = null,
            InMemorySettingsStore? store = null,
            bool queueUiWork = false)
        {
            _serverClockOffsetMs = serverClockOffsetMs;
            Time = time ?? new FakeTimeProvider(NOW);
            Store = store ?? new InMemorySettingsStore();
            Api = new Mock<IDisplayManagementApi>();
            ListReturns();
            Action<Action>? runOnUiThread = queueUiWork ? UiQueue.Add : null;
            Displays = new DisplaysViewModel(
                Api.Object,
                Time,
                () => _serverClockOffsetMs,
                runOnUiThread,
                new PendingPairCodeStore(Store));
            if (signedIn)
            {
                Displays.SetSignedIn(true);
            }
        }

        public FakeTimeProvider Time { get; }

        public InMemorySettingsStore Store { get; }

        /// <summary>Work handed to the UI thread when the fixture queues it instead of running it.</summary>
        public List<Action> UiQueue { get; } = new();

        /// <summary>The same device after the page reloads: same clock and storage, nothing in memory.</summary>
        public DisplaysFixture Reload(bool signedIn) => new(signedIn, _serverClockOffsetMs, Time, Store);

        public void RunUiQueue()
        {
            List<Action> work = UiQueue.ToList();
            UiQueue.Clear();
            foreach (Action action in work)
            {
                action();
            }
        }

        public Mock<IDisplayManagementApi> Api { get; }

        public DisplaysViewModel Displays { get; }

        public void ListReturns(params DisplayInfoPayload[] displays)
        {
            List<DisplayInfoPayload> list = displays.ToList();
            Api
                .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DisplayListResult(DisplayManagementStatus.Succeeded, list));
        }

        public void ApprovalReturns(DisplayManagementStatus status, DisplayInfoPayload? display = null)
        {
            Api
                .Setup(a => a.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DisplayApprovalResult(status, display));
        }
    }
}
