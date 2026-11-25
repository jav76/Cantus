using System.Net;
using System.Net.Http.Headers;
using Cantus.Infrastructure.CoverArt;
using FluentAssertions;

namespace Cantus.Infrastructure.Tests.CoverArt;

public class CoverArtArchiveClientTests
{
    private static readonly Guid ReleaseGroupId = Guid.Parse("9162580e-5df4-32de-80cc-f45a8d8a9b1d");

    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task FindFrontCoverAsync_WhenTheThumbnailExists_ReturnsTheArchivesOwnUrl()
    {
        _handler.ResponseHandler = _ => Image();

        string? artUrl = await CreateClient().FindFrontCoverAsync(ReleaseGroupId);

        artUrl.Should().Be(
            "https://coverartarchive.test/release-group/9162580e-5df4-32de-80cc-f45a8d8a9b1d/front-500");
        HttpRequestMessage request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Head, "the image itself is never downloaded by the server");
        request.RequestUri!.AbsoluteUri.Should().Be(artUrl);
    }

    [Fact]
    public async Task FindFrontCoverAsync_WhenTheArchiveHasNoCover_ReturnsNull()
    {
        _handler.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        string? artUrl = await CreateClient().FindFrontCoverAsync(ReleaseGroupId);

        artUrl.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task FindFrontCoverAsync_WhenTheArchiveFails_Throws(HttpStatusCode statusCode)
    {
        _handler.ResponseHandler = _ => new HttpResponseMessage(statusCode);

        Func<Task> act = () => CreateClient().FindFrontCoverAsync(ReleaseGroupId);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FindFrontCoverAsync_WhenTheAnswerIsNotAnImage_Throws()
    {
        _handler.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>maintenance</html>", new MediaTypeHeaderValue("text/html"))
        };

        Func<Task> act = () => CreateClient().FindFrontCoverAsync(ReleaseGroupId);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private CoverArtArchiveClient CreateClient() =>
        new(new HttpClient(_handler) { BaseAddress = new Uri("https://coverartarchive.test") });

    private static HttpResponseMessage Image()
    {
        ByteArrayContent content = new([]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
