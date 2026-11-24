using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cantus.Server.Tests.Middleware;

public sealed class PrecompressedStaticFilesTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string WASM_PATH = "/_framework/dotnet.native.wasm";
    private const string PLAIN_PATH = "/_framework/blazor.boot.json";
    private const string ORIGINAL_BODY = "original";
    private const string BROTLI_BODY = "brotli";
    private const string GZIP_BODY = "gzip";
    private const string INDEX_BODY = "<html>index</html>";
    private const string INDEX_BROTLI_BODY = "index-brotli";

    private readonly string _webRoot;
    private readonly WebApplicationFactory<Program> _factory;

    public PrecompressedStaticFilesTests(WebApplicationFactory<Program> factory)
    {
        _webRoot = Directory.CreateTempSubdirectory("cantus-webroot-").FullName;
        string framework = Directory.CreateDirectory(Path.Combine(_webRoot, "_framework")).FullName;

        File.WriteAllText(Path.Combine(framework, "dotnet.native.wasm"), ORIGINAL_BODY);
        File.WriteAllText(Path.Combine(framework, "dotnet.native.wasm.br"), BROTLI_BODY);
        File.WriteAllText(Path.Combine(framework, "dotnet.native.wasm.gz"), GZIP_BODY);
        File.WriteAllText(Path.Combine(framework, "blazor.boot.json"), "{}");
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), INDEX_BODY);
        File.WriteAllText(Path.Combine(_webRoot, "index.html.br"), INDEX_BROTLI_BODY);

        _factory = factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot));
    }

    public void Dispose()
    {
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("br", "br", BROTLI_BODY)]
    [InlineData("gzip, deflate, br, zstd", "br", BROTLI_BODY)]
    [InlineData("gzip", "gzip", GZIP_BODY)]
    [InlineData("br;q=0, gzip", "gzip", GZIP_BODY)]
    [InlineData("*", "br", BROTLI_BODY)]
    public async Task Get_AcceptedEncoding_ServesCompressedFileWithOriginalContentType(
        string acceptEncoding, string expectedEncoding, string expectedBody)
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, WASM_PATH, acceptEncoding);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Equal(expectedEncoding);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/wasm");
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        (await response.Content.ReadAsStringAsync()).Should().Be(expectedBody);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("identity")]
    [InlineData("br;q=0, gzip;q=0")]
    public async Task Get_NoAcceptedEncoding_ServesOriginalFile(string? acceptEncoding)
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, WASM_PATH, acceptEncoding);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/wasm");
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        (await response.Content.ReadAsStringAsync()).Should().Be(ORIGINAL_BODY);
    }

    [Fact]
    public async Task Get_FileWithoutCompressedSibling_ServesOriginalWithoutVary()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, PLAIN_PATH, "br, gzip");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        response.Headers.Vary.Should().NotContain("Accept-Encoding");
        (await response.Content.ReadAsStringAsync()).Should().Be("{}");
    }

    [Fact]
    public async Task Head_AcceptedEncoding_ReturnsCompressedHeaders()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Head, WASM_PATH, "br");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Equal("br");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/wasm");
        response.Content.Headers.ContentLength.Should().Be(Encoding.UTF8.GetByteCount(BROTLI_BODY));
    }

    [Fact]
    public async Task Get_IndexHtml_ServesCompressedHtml()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/index.html", "br");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Equal("br");
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Be(INDEX_BROTLI_BODY);
    }

    [Fact]
    public async Task Get_ClientRoute_StillFallsBackToIndex()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/kiosk/display", "br");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Be(INDEX_BODY);
    }

    [Fact]
    public async Task Get_CompressedFileRequestedDirectly_IsNotEncodedTwice()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, WASM_PATH + ".br", "br");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        (await response.Content.ReadAsStringAsync()).Should().Be(BROTLI_BODY);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? acceptEncoding)
    {
        HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = new(method, path);
        if (acceptEncoding is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        }

        return await client.SendAsync(request);
    }
}
