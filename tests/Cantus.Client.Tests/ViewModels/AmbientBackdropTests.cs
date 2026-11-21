using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class AmbientBackdropTests
{
    private const string FIRST_URL = "https://i.scdn.co/image/first";
    private const string SECOND_URL = "https://i.scdn.co/image/second";

    private readonly Dictionary<string, TaskCompletionSource<string?>> _pendingBlurs = new();
    private readonly List<string> _blurRequests = [];
    private int _sourceChangedCount;

    [Fact]
    public void BlurOff_ShowsTheSharpArtworkWithoutBlurring()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();

        // Act
        backdrop.Update(FIRST_URL, isBlurEnabled: false);

        // Assert
        backdrop.Source.Should().Be(Sharp(FIRST_URL));
        backdrop.IsBlurred.Should().BeFalse();
        _blurRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task BlurOn_ShowsSharpFirstThenTheBlurredCopy()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();

        // Act
        backdrop.Update(FIRST_URL, isBlurEnabled: true);
        string? whilePending = backdrop.Source;
        await CompleteBlurAsync(backdrop, FIRST_URL);

        // Assert
        whilePending.Should().Be(Sharp(FIRST_URL));
        backdrop.Source.Should().Be(Blurred(FIRST_URL));
        backdrop.IsBlurred.Should().BeTrue();
    }

    [Fact]
    public async Task SlowBlurForAnOldTrack_NeverReplacesTheNewerBackdrop()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();
        backdrop.Update(FIRST_URL, isBlurEnabled: true);
        Task firstBlur = backdrop.PendingBlur!;

        // Act
        backdrop.Update(SECOND_URL, isBlurEnabled: true);
        _pendingBlurs[FIRST_URL].SetResult(Blurred(FIRST_URL));
        await firstBlur;

        // Assert
        backdrop.Source.Should().Be(Sharp(SECOND_URL));
        backdrop.IsBlurred.Should().BeFalse();
    }

    [Fact]
    public async Task TogglingBlurOff_SwapsToSharpAtOnceAndIgnoresTheLateBlur()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();
        backdrop.Update(FIRST_URL, isBlurEnabled: true);
        Task pending = backdrop.PendingBlur!;

        // Act
        backdrop.Update(FIRST_URL, isBlurEnabled: false);
        _pendingBlurs[FIRST_URL].SetResult(Blurred(FIRST_URL));
        await pending;

        // Assert
        backdrop.Source.Should().Be(Sharp(FIRST_URL));
        backdrop.IsBlurred.Should().BeFalse();
    }

    [Fact]
    public async Task TogglingBlurBackOn_ReusesTheBlurredCopyForTheSameArtwork()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();
        backdrop.Update(FIRST_URL, isBlurEnabled: true);
        await CompleteBlurAsync(backdrop, FIRST_URL);

        // Act
        backdrop.Update(FIRST_URL, isBlurEnabled: false);
        string? whileOff = backdrop.Source;
        backdrop.Update(FIRST_URL, isBlurEnabled: true);

        // Assert
        whileOff.Should().Be(Sharp(FIRST_URL));
        backdrop.Source.Should().Be(Blurred(FIRST_URL));
        _blurRequests.Should().ContainSingle();
    }

    [Fact]
    public void ArtworkCleared_HidesTheBackdrop()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();
        backdrop.Update(FIRST_URL, isBlurEnabled: true);

        // Act
        backdrop.Update(null, isBlurEnabled: true);

        // Assert
        backdrop.Source.Should().BeNull();
        backdrop.IsBlurred.Should().BeFalse();
    }

    [Fact]
    public async Task FailedBlur_KeepsTheSharpArtwork()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();
        backdrop.Update(FIRST_URL, isBlurEnabled: true);
        Task pending = backdrop.PendingBlur!;

        // Act
        _pendingBlurs[FIRST_URL].SetException(new InvalidOperationException("decode failed"));
        await pending;

        // Assert
        backdrop.Source.Should().Be(Sharp(FIRST_URL));
        backdrop.IsBlurred.Should().BeFalse();
    }

    [Fact]
    public async Task NullBlur_KeepsTheSharpArtwork()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();
        backdrop.Update(FIRST_URL, isBlurEnabled: true);
        Task pending = backdrop.PendingBlur!;

        // Act
        _pendingBlurs[FIRST_URL].SetResult(null);
        await pending;

        // Assert
        backdrop.Source.Should().Be(Sharp(FIRST_URL));
    }

    [Fact]
    public void SharpImageThatCannotBeCreated_LeavesNoBackdrop()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = new(
            _ => throw new InvalidOperationException("no XAML"),
            (_, _) => Task.FromResult<string?>(null),
            NullLogger.Instance);

        // Act
        backdrop.Update(FIRST_URL, isBlurEnabled: false);

        // Assert
        backdrop.Source.Should().BeNull();
    }

    [Fact]
    public void RepeatedUpdates_RaiseSourceChangedOnlyOnChange()
    {
        // Arrange
        AmbientBackdrop<string> backdrop = CreateBackdrop();

        // Act
        backdrop.Update(FIRST_URL, isBlurEnabled: false);
        backdrop.Update(FIRST_URL, isBlurEnabled: false);

        // Assert
        _sourceChangedCount.Should().Be(1);
    }

    private AmbientBackdrop<string> CreateBackdrop()
    {
        AmbientBackdrop<string> backdrop = new(Sharp, CreateBlurredAsync, NullLogger.Instance);
        backdrop.SourceChanged += () => _sourceChangedCount++;
        return backdrop;
    }

    private Task<string?> CreateBlurredAsync(string url, CancellationToken cancellationToken)
    {
        _blurRequests.Add(url);
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingBlurs[url] = completion;
        return completion.Task;
    }

    private async Task CompleteBlurAsync(AmbientBackdrop<string> backdrop, string url)
    {
        Task pending = backdrop.PendingBlur!;
        _pendingBlurs[url].SetResult(Blurred(url));
        await pending;
    }

    private static string Sharp(string url) => $"sharp:{url}";

    private static string Blurred(string url) => $"blurred:{url}";
}
