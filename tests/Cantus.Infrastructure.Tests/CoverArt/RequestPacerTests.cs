using Cantus.Infrastructure.CoverArt;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Cantus.Infrastructure.Tests.CoverArt;

public sealed class RequestPacerTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly RequestPacer _pacer;

    public RequestPacerTests()
    {
        _pacer = new RequestPacer(_timeProvider, Interval);
    }

    public void Dispose() => _pacer.Dispose();

    [Fact]
    public void WaitForTurnAsync_ForTheFirstRequest_CompletesAtOnce()
    {
        _pacer.WaitForTurnAsync().IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForTurnAsync_ForASecondRequest_WaitsOutTheInterval()
    {
        await _pacer.WaitForTurnAsync();

        Task second = _pacer.WaitForTurnAsync();
        second.IsCompleted.Should().BeFalse();

        _timeProvider.Advance(Interval - TimeSpan.FromMilliseconds(1));
        second.IsCompleted.Should().BeFalse();

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForTurnAsync_AfterTheIntervalHasPassed_CompletesAtOnce()
    {
        await _pacer.WaitForTurnAsync();
        _timeProvider.Advance(Interval);

        _pacer.WaitForTurnAsync().IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForTurnAsync_WhenCancelledWhileWaiting_LetsTheNextCallerThrough()
    {
        await _pacer.WaitForTurnAsync();
        using CancellationTokenSource cancellation = new();
        Task waiting = _pacer.WaitForTurnAsync(cancellation.Token);

        await cancellation.CancelAsync();
        Func<Task> act = () => waiting;
        await act.Should().ThrowAsync<OperationCanceledException>();

        _timeProvider.Advance(Interval);
        await _pacer.WaitForTurnAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RequestPacingHandler_HoldsARequestUntilItsTurn()
    {
        StubHttpMessageHandler inner = new();
        using HttpClient client = new(new RequestPacingHandler(_pacer) { InnerHandler = inner })
        {
            BaseAddress = new Uri("https://musicbrainz.test")
        };

        await client.GetAsync("/first");
        Task<HttpResponseMessage> second = client.GetAsync("/second");

        inner.Requests.Should().ContainSingle();

        _timeProvider.Advance(Interval);
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        inner.Requests.Should().HaveCount(2);
    }
}
