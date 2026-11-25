using System.Net;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LocalServerApiTests
{
    private const string BASE_URL = "http://127.0.0.1:5000";

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetVersionAsync_CantusServer_ReturnsItsVersion(HttpStatusCode status)
    {
        StubHandler handler = new(_ => Json(status, """{"status":"Healthy","version":"1.2.0"}"""));
        LocalServerApi api = new(new HttpClient(handler));

        string? version = await api.GetVersionAsync(BASE_URL);

        version.Should().Be("1.2.0");
        handler.Requests.Should().ContainSingle().Which.RequestUri!.ToString().Should().Be($"{BASE_URL}/api/health");
    }

    [Theory]
    [InlineData("<html>not cantus</html>")]
    [InlineData("""{"status":"ok"}""")]
    [InlineData("[]")]
    public async Task GetVersionAsync_SomethingElseOnThePort_IsNull(string body)
    {
        LocalServerApi api = new(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, body))));

        (await api.GetVersionAsync(BASE_URL)).Should().BeNull();
    }

    [Fact]
    public async Task GetVersionAsync_NothingListening_IsNull()
    {
        LocalServerApi api = new(new HttpClient(new StubHandler(_ => throw new HttpRequestException("refused"))));

        (await api.GetVersionAsync(BASE_URL)).Should().BeNull();
    }

    [Fact]
    public async Task RequestShutdownAsync_SendsTheControlToken()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        LocalServerApi api = new(new HttpClient(handler));

        bool accepted = await api.RequestShutdownAsync(BASE_URL, "secret-token");

        accepted.Should().BeTrue();
        HttpRequestMessage request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be($"{BASE_URL}/api/local-server/shutdown");
        request.Headers.GetValues("X-Cantus-Local-Token").Should().Equal("secret-token");
    }

    [Fact]
    public async Task RequestShutdownAsync_Refused_IsFalse()
    {
        LocalServerApi api = new(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        (await api.RequestShutdownAsync(BASE_URL, "wrong")).Should().BeFalse();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(_respond(request));
        }
    }
}
