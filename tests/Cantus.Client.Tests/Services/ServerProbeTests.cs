using System.Net;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ServerProbeTests
{
    [Fact]
    public async Task IsReachableAsync_HealthyServer_IsReachable()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        ServerProbe probe = new(new HttpClient(handler));

        bool reachable = await probe.IsReachableAsync("http://192.168.0.10:5000");

        reachable.Should().BeTrue();
        handler.RequestedUri.Should().Be("http://192.168.0.10:5000/api/health");
    }

    [Fact]
    public async Task IsReachableAsync_ErrorStatus_IsNotReachable()
    {
        ServerProbe probe = new(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        (await probe.IsReachableAsync("http://192.168.0.10:5000")).Should().BeFalse();
    }

    [Fact]
    public async Task IsReachableAsync_ConnectionFailure_IsNotReachable()
    {
        ServerProbe probe = new(new HttpClient(new StubHandler(_ => throw new HttpRequestException("refused"))));

        (await probe.IsReachableAsync("http://192.168.0.10:5000")).Should().BeFalse();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public string? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUri = request.RequestUri?.ToString();

            return Task.FromResult(_respond(request));
        }
    }
}
