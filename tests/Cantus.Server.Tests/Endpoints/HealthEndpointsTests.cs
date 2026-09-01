using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Cantus.Server.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WithValidStatusPayload()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        HealthResponseDto? result = await response.Content.ReadFromJsonAsync<HealthResponseDto>();

        result.Should().NotBeNull();
        result!.Status.Should().Be("Healthy");
        result.Database.Should().Be("Connected");
        result.Version.Should().NotBeNullOrWhiteSpace();
        result.ActiveSessions.Should().BeGreaterThanOrEqualTo(0);
    }
}
