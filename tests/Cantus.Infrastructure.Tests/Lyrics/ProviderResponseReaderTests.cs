using System.Net;
using System.Text;
using System.Text.Json;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public sealed class ProviderResponseReaderTests
{
    // Reports no Content-Length, so only the streamed byte count can enforce the cap.
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _body;

        public UnknownLengthContent(string body)
        {
            _body = Encoding.UTF8.GetBytes(body);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_body, 0, _body.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private sealed record Payload(int Code);

    [Fact]
    public async Task ReadJsonAsync_BodyWithinCap_Deserializes()
    {
        using HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent("""{ "code": 200 }""")
        };

        Payload? payload = await ProviderResponseReader.ReadJsonAsync<Payload>(
            response,
            Options,
            "Test",
            CancellationToken.None);

        payload.Should().Be(new Payload(200));
    }

    [Fact]
    public async Task ReadJsonAsync_StreamedBodyOverCap_Throws()
    {
        string padding = new('x', ProviderResponseReader.MAX_LOOKUP_RESPONSE_BYTES);
        using HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent($$"""{ "code": 200, "padding": "{{padding}}" }""")
        };

        Func<Task> read = () => ProviderResponseReader.ReadJsonAsync<Payload>(
            response,
            Options,
            "Test",
            CancellationToken.None);

        (await read.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("Test");
    }
}
