using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LocalSessionApiTests
{
    private const string SERVER_BASE_URL = "http://192.168.0.10:5000";

    [Fact]
    public async Task Create_PostsTheNameAndJoinCodeAndReturnsTheSession()
    {
        // Arrange
        StubHandler handler = new(HttpStatusCode.OK, """
            { "id": "session-local-1", "spotifyUserId": null, "kind": "Local", "displayName": "jaret-desktop" }
            """);
        LocalSessionApi api = CreateApi(handler);

        // Act
        LocalSessionResult result = await api.CreateAsync("jaret-desktop", " K7QH2MZP9 ");

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Created);
        result.Session!.Id.Should().Be("session-local-1");
        result.Session.IsLocal.Should().BeTrue();
        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri.Should().Be($"{SERVER_BASE_URL}/api/auth/local");
        handler.LastBody.Should().Be("""{"displayName":"jaret-desktop","joinCode":"K7QH2MZP9"}""");
    }

    [Fact]
    public async Task Create_WithoutAJoinCode_SendsNull()
    {
        // Arrange
        StubHandler handler = new(HttpStatusCode.OK, """{ "id": "s", "kind": "Local" }""");

        // Act
        await CreateApi(handler).CreateAsync("pc", "  ");

        // Assert
        handler.LastBody.Should().Be("""{"displayName":"pc","joinCode":null}""");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{ "error": "invalid_display_name" }""", LocalSessionStatus.InvalidDisplayName)]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "join_code_required" }""", LocalSessionStatus.JoinCodeRequired)]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "join_code_invalid" }""", LocalSessionStatus.JoinCodeInvalid)]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "something_else" }""", LocalSessionStatus.Failed)]
    [InlineData(HttpStatusCode.Forbidden, """{ "error": "local_sessions_disabled" }""", LocalSessionStatus.Disabled)]
    [InlineData(HttpStatusCode.TooManyRequests, "", LocalSessionStatus.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{ "error": "local_session_limit" }""", LocalSessionStatus.LimitReached)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>proxy down</html>", LocalSessionStatus.Failed)]
    [InlineData(HttpStatusCode.NotFound, "", LocalSessionStatus.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", LocalSessionStatus.Failed)]
    public async Task Create_MapsEachRefusal(HttpStatusCode statusCode, string body, LocalSessionStatus expected)
    {
        // Act
        LocalSessionResult result = await CreateApi(new StubHandler(statusCode, body)).CreateAsync("pc", null);

        // Assert
        result.Status.Should().Be(expected);
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task Create_WithASuccessButNoSessionId_Fails()
    {
        // Act
        LocalSessionResult result = await CreateApi(new StubHandler(HttpStatusCode.OK, """{ "id": "" }""")).CreateAsync("pc", null);

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Failed);
    }

    [Fact]
    public async Task Create_WhenTheServerIsUnreachable_Fails()
    {
        // Act
        LocalSessionResult result = await CreateApi(new StubHandler(new HttpRequestException("connection refused")))
            .CreateAsync("pc", null);

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Failed);
    }

    private static LocalSessionApi CreateApi(StubHandler handler)
    {
        Mock<ISignalRPlaybackClient> client = new();
        client.SetupGet(c => c.ServerBaseUrl).Returns(SERVER_BASE_URL);
        return new LocalSessionApi(client.Object, new HttpClient(handler));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;
        private readonly Exception? _failure;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public StubHandler(Exception failure)
        {
            _failure = failure;
            _body = string.Empty;
        }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_failure is not null)
            {
                throw _failure;
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
