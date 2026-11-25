using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Server.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class LocalServerEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CONTROL_TOKEN = "0123456789ABCDEF0123456789ABCDEF";

    private readonly WebApplicationFactory<Program> _factory;

    public LocalServerEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Shutdown_WithoutAControlTokenConfigured_IsNotOffered()
    {
        HttpClient client = _factory.CreateClient();
        CancellationToken stopping = GetStoppingToken(_factory);

        HttpResponseMessage response = await SendShutdownAsync(client, CONTROL_TOKEN);

        response.IsSuccessStatusCode.Should().BeFalse();
        stopping.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-token")]
    public async Task Shutdown_WithoutTheRightToken_IsRefused(string? presentedToken)
    {
        using WebApplicationFactory<Program> factory = CreateLocalServerFactory();
        HttpClient client = factory.CreateClient();
        CancellationToken stopping = GetStoppingToken(factory);

        HttpResponseMessage response = await SendShutdownAsync(client, presentedToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        stopping.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Shutdown_WithTheRightToken_StopsTheServer()
    {
        using WebApplicationFactory<Program> factory = CreateLocalServerFactory();
        HttpClient client = factory.CreateClient();
        CancellationToken stopping = GetStoppingToken(factory);

        HttpResponseMessage response = await SendShutdownAsync(client, CONTROL_TOKEN);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        stopping.IsCancellationRequested.Should().BeTrue();
    }

    private WebApplicationFactory<Program> CreateLocalServerFactory() =>
        _factory.WithWebHostBuilder(builder => builder.UseSetting(LocalServerEndpoints.CONTROL_TOKEN_KEY, CONTROL_TOKEN));

    private static Task<HttpResponseMessage> SendShutdownAsync(HttpClient client, string? presentedToken)
    {
        HttpRequestMessage request = new(HttpMethod.Post, LocalServerEndpoints.SHUTDOWN_PATH);
        if (presentedToken is not null)
        {
            request.Headers.TryAddWithoutValidation(LocalServerEndpoints.CONTROL_TOKEN_HEADER, presentedToken);
        }

        return client.SendAsync(request);
    }

    // Read before the request: a stopped host disposes its services.
    private static CancellationToken GetStoppingToken(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
}
