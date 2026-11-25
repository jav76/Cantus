using System.Net;

namespace Cantus.Infrastructure.Tests.CoverArt;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> ResponseHandler { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(ResponseHandler(request));
    }
}
