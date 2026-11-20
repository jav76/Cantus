using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class DisplayManagementApiTests
{
    private const string SERVER_BASE_URL = "http://192.168.0.10:5000";
    private const string SESSION_TOKEN = "session-secret";

    [Fact]
    public async Task Approve_PostsTheCodeAndNameWithTheSessionToken()
    {
        // Arrange
        ApiFixture fixture = new(HttpStatusCode.Created, """
            { "id": "display-1", "name": "Living room TV", "createdAtUtc": "2026-11-20T18:01:12Z", "lastSeenAtUtc": null }
            """);

        // Act
        DisplayApprovalResult result = await fixture.Api.ApproveAsync("K7QH2MZP", "Living room TV");

        // Assert
        result.Status.Should().Be(DisplayManagementStatus.Succeeded);
        result.Display!.Id.Should().Be("display-1");
        result.Display.Name.Should().Be("Living room TV");
        result.Display.LastSeenAtUtc.Should().BeNull();
        fixture.LastMethod.Should().Be(HttpMethod.Post);
        fixture.LastUri.Should().Be($"{SERVER_BASE_URL}/api/displays/pairings/approve");
        fixture.LastAuthorization.Should().Be($"Bearer {SESSION_TOKEN}");
        fixture.LastBody.Should().Contain("\"code\":\"K7QH2MZP\"").And.Contain("\"name\":\"Living room TV\"");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, DisplayManagementStatus.InvalidName)]
    [InlineData(HttpStatusCode.Unauthorized, DisplayManagementStatus.NotSignedIn)]
    [InlineData(HttpStatusCode.Forbidden, DisplayManagementStatus.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, DisplayManagementStatus.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, DisplayManagementStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, DisplayManagementStatus.Failed)]
    public async Task Approve_MapsRefusalsToAStatus(HttpStatusCode statusCode, DisplayManagementStatus expected)
    {
        // Arrange
        ApiFixture fixture = new(statusCode, "{}");

        // Act
        DisplayApprovalResult result = await fixture.Api.ApproveAsync("K7QH2MZP", "TV");

        // Assert
        result.Status.Should().Be(expected);
        result.Display.Should().BeNull();
    }

    [Fact]
    public async Task Approve_WhenTheServerIsUnreachable_Fails()
    {
        // Arrange
        ApiFixture fixture = new(new HttpRequestException("connection refused"));

        // Act
        DisplayApprovalResult result = await fixture.Api.ApproveAsync("K7QH2MZP", "TV");

        // Assert
        result.Status.Should().Be(DisplayManagementStatus.Failed);
    }

    [Fact]
    public async Task List_ReturnsTheOwnersDisplays()
    {
        // Arrange
        ApiFixture fixture = new(HttpStatusCode.OK, """
            [
              { "id": "display-1", "name": "Living room TV", "createdAtUtc": "2026-11-20T18:01:12Z", "lastSeenAtUtc": "2026-11-20T19:00:00Z" },
              { "id": "display-2", "name": "Kitchen", "createdAtUtc": "2026-11-20T18:05:00Z", "lastSeenAtUtc": null }
            ]
            """);

        // Act
        DisplayListResult result = await fixture.Api.ListAsync();

        // Assert
        result.Status.Should().Be(DisplayManagementStatus.Succeeded);
        result.Displays.Should().HaveCount(2);
        result.Displays![0].LastSeenAtUtc.Should().Be(new DateTimeOffset(2026, 11, 20, 19, 0, 0, TimeSpan.Zero));
        result.Displays[1].Name.Should().Be("Kitchen");
        fixture.LastMethod.Should().Be(HttpMethod.Get);
        fixture.LastUri.Should().Be($"{SERVER_BASE_URL}/api/displays");
    }

    [Fact]
    public async Task List_WithoutASessionToken_SendsNoAuthorizationHeader()
    {
        // Arrange
        ApiFixture fixture = new(HttpStatusCode.OK, "[]", sessionToken: null);

        // Act
        DisplayListResult result = await fixture.Api.ListAsync();

        // Assert
        result.Status.Should().Be(DisplayManagementStatus.Succeeded);
        result.Displays.Should().BeEmpty();
        fixture.LastAuthorization.Should().BeNull();
    }

    [Fact]
    public async Task List_WhenRefused_ReturnsNoDisplays()
    {
        // Arrange
        ApiFixture fixture = new(HttpStatusCode.Unauthorized, "{}");

        // Act
        DisplayListResult result = await fixture.Api.ListAsync();

        // Assert
        result.Status.Should().Be(DisplayManagementStatus.NotSignedIn);
        result.Displays.Should().BeNull();
    }

    [Fact]
    public async Task Rename_PatchesTheDisplay()
    {
        // Arrange
        ApiFixture fixture = new(HttpStatusCode.NoContent, string.Empty);

        // Act
        DisplayManagementStatus status = await fixture.Api.RenameAsync("display/1", "Bedroom");

        // Assert
        status.Should().Be(DisplayManagementStatus.Succeeded);
        fixture.LastMethod.Should().Be(HttpMethod.Patch);
        fixture.LastUri.Should().Be($"{SERVER_BASE_URL}/api/displays/display%2F1");
        fixture.LastBody.Should().Be("{\"name\":\"Bedroom\"}");
    }

    [Fact]
    public async Task Revoke_DeletesTheDisplay()
    {
        // Arrange
        ApiFixture fixture = new(HttpStatusCode.NoContent, string.Empty);

        // Act
        DisplayManagementStatus status = await fixture.Api.RevokeAsync("display-1");

        // Assert
        status.Should().Be(DisplayManagementStatus.Succeeded);
        fixture.LastMethod.Should().Be(HttpMethod.Delete);
        fixture.LastUri.Should().Be($"{SERVER_BASE_URL}/api/displays/display-1");
        fixture.LastAuthorization.Should().Be($"Bearer {SESSION_TOKEN}");
    }

    [Fact]
    public async Task Revoke_OfAnUnknownDisplay_IsNotFound()
    {
        // Arrange
        ApiFixture fixture = new(HttpStatusCode.NotFound, "{}");

        // Act
        DisplayManagementStatus status = await fixture.Api.RevokeAsync("display-1");

        // Assert
        status.Should().Be(DisplayManagementStatus.NotFound);
    }

    [Fact]
    public async Task Requests_WithAnUnusableServerAddress_Fail()
    {
        // Arrange
        Mock<ISignalRPlaybackClient> client = new();
        client.SetupGet(c => c.ServerBaseUrl).Returns(string.Empty);
        DisplayManagementApi api = new(client.Object, new HttpClient(new StubHandler(HttpStatusCode.OK, "[]")));

        // Act
        DisplayListResult result = await api.ListAsync();

        // Assert
        result.Status.Should().Be(DisplayManagementStatus.Failed);
    }

    private sealed class ApiFixture
    {
        private readonly StubHandler _handler;

        public ApiFixture(HttpStatusCode statusCode, string body, string? sessionToken = SESSION_TOKEN)
            : this(new StubHandler(statusCode, body), sessionToken)
        {
        }

        public ApiFixture(Exception failure)
            : this(new StubHandler(failure), SESSION_TOKEN)
        {
        }

        private ApiFixture(StubHandler handler, string? sessionToken)
        {
            _handler = handler;
            Mock<ISignalRPlaybackClient> client = new();
            client.SetupGet(c => c.ServerBaseUrl).Returns(SERVER_BASE_URL);
            client.SetupGet(c => c.SessionToken).Returns(sessionToken);
            Api = new DisplayManagementApi(client.Object, new HttpClient(handler));
        }

        public DisplayManagementApi Api { get; }

        public HttpMethod? LastMethod => _handler.LastMethod;

        public string? LastUri => _handler.LastUri;

        public string? LastAuthorization => _handler.LastAuthorization;

        public string? LastBody => _handler.LastBody;
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

        public string? LastAuthorization { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri?.AbsoluteUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_failure is not null)
            {
                throw _failure;
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
