using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Sessions;
using Cantus.Server.Endpoints;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

/// <summary>
/// <c>POST /api/auth/local</c>: off by default, optionally gated by a join code, rate limited
/// per client address and capped. A created Session is a credential like a Spotify one.
/// </summary>
public sealed class LocalSessionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LOCAL_PATH = "/api/auth/local";
    private const string JOIN_CODE = "correct-horse";

    private readonly WebApplicationFactory<Program> _factory;

    public LocalSessionEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_ByDefault_IsForbiddenBecauseLocalSessionsAreDisabled()
    {
        await using DisplayTestHost host = new(_factory);

        HttpResponseMessage response = await PostAsync(host, "Desktop");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorOf(response)).Should().Be(AuthEndpoints.ERROR_LOCAL_SESSIONS_DISABLED);
    }

    [Fact]
    public async Task Create_WhenEnabled_ReturnsALocalSessionAndSetsTheSessionCookie()
    {
        await using DisplayTestHost host = CreateHost();

        HttpResponseMessage response = await PostAsync(host, " jaret-desktop ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AuthorizedSessionDto session = (await response.Content.ReadFromJsonAsync<AuthorizedSessionDto>())!;
        session.Kind.Should().Be(AuthorizedSessionDto.KIND_LOCAL);
        session.SpotifyUserId.Should().BeNull();
        session.DisplayName.Should().Be("jaret-desktop");

        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).Should().BeTrue();
        string cookie = cookies!.Single(c => c.StartsWith(DisplayTestHost.SESSION_COOKIE + "=", StringComparison.Ordinal));
        cookie.Should().Contain($"{DisplayTestHost.SESSION_COOKIE}={session.Id}");
        cookie.Should().Contain("httponly").And.Contain("samesite=lax");

        UserSessionEntity? row = await host.Store.FindSessionAsync(session.Id);
        row!.Kind.Should().Be(SessionKind.Local);
    }

    [Fact]
    public async Task Create_ReturnsASessionIdThatWorksAsACredential()
    {
        await using DisplayTestHost host = CreateHost();
        AuthorizedSessionDto created = (await (await PostAsync(host, "Desktop"))
            .Content.ReadFromJsonAsync<AuthorizedSessionDto>())!;

        HttpResponseMessage me = await host.CreateClient(DisplayTestHost.SESSION_COOKIE, created.Id).GetAsync("/api/auth/me");

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<AuthorizedSessionDto>())!.Kind.Should().Be(AuthorizedSessionDto.KIND_LOCAL);
    }

    [Theory]
    [InlineData(null, AuthEndpoints.ERROR_JOIN_CODE_REQUIRED)]
    [InlineData("", AuthEndpoints.ERROR_JOIN_CODE_REQUIRED)]
    [InlineData("wrong-horse", AuthEndpoints.ERROR_JOIN_CODE_INVALID)]
    [InlineData("correct-hors", AuthEndpoints.ERROR_JOIN_CODE_INVALID)]
    public async Task Create_WithJoinCodeConfigured_RejectsAMissingOrWrongCode(string? joinCode, string expectedError)
    {
        await using DisplayTestHost host = CreateHost(options => options.JoinCode = JOIN_CODE);

        HttpResponseMessage response = await PostAsync(host, "Desktop", joinCode);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ErrorOf(response)).Should().Be(expectedError);
    }

    [Fact]
    public async Task Create_WithTheRightJoinCode_Succeeds()
    {
        await using DisplayTestHost host = CreateHost(options => options.JoinCode = JOIN_CODE);

        HttpResponseMessage response = await PostAsync(host, "Desktop", JOIN_CODE);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("this-name-is-far-too-long-to-be-a-display-name-because-it-has-65-c")]
    public async Task Create_WithAnInvalidName_IsBadRequest(string? displayName)
    {
        await using DisplayTestHost host = CreateHost();

        HttpResponseMessage response = await PostAsync(host, displayName);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorOf(response)).Should().Be(AuthEndpoints.ERROR_INVALID_DISPLAY_NAME);
    }

    [Fact]
    public async Task Create_AtTheCap_IsServiceUnavailable()
    {
        await using DisplayTestHost host = CreateHost(options => options.MaxSessions = 1);
        (await PostAsync(host, "First")).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage response = await PostAsync(host, "Second");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorOf(response)).Should().Be(AuthEndpoints.ERROR_LOCAL_SESSION_LIMIT);
    }

    [Fact]
    public async Task Create_OverTheRateLimit_IsTooManyRequests()
    {
        const int PERMITS = 2;
        await using DisplayTestHost host = CreateHost(options => options.RateLimitPermitLimit = PERMITS);
        for (int i = 0; i < PERMITS; i++)
        {
            (await PostAsync(host, $"Desktop {i}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        HttpResponseMessage response = await PostAsync(host, "One more");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Logout_OfALocalSession_DeletesItsDisplaysAndDropsItsPlaybackState()
    {
        await using DisplayTestHost host = CreateHost();
        AuthorizedSessionDto created = (await (await PostAsync(host, "Desktop"))
            .Content.ReadFromJsonAsync<AuthorizedSessionDto>())!;
        CreatedDisplay tv = await host.Store.CreateDisplayService().CreateAsync(created.Id, "TV");
        IPlaybackSessionRegistry registry = host.Factory.Services.GetRequiredService<IPlaybackSessionRegistry>();
        registry.UpdateUserState(created.Id, "Desktop", new PlaybackState { Source = PlaybackSource.Local }, null, 0);

        HttpResponseMessage response = await host
            .CreateClient(DisplayTestHost.SESSION_COOKIE, created.Id)
            .PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Store.SessionExistsAsync(created.Id)).Should().BeFalse();
        (await host.Store.CreateDisplayService().ValidateAsync(tv.Token)).Should().BeNull();
        registry.GetUserState(created.Id).Should().BeNull();
    }

    private DisplayTestHost CreateHost(Action<LocalSessionOptions>? configure = null)
    {
        return new DisplayTestHost(_factory, configureServices: services => services.Configure<LocalSessionOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        }));
    }

    private static Task<HttpResponseMessage> PostAsync(DisplayTestHost host, string? displayName, string? joinCode = null)
    {
        return host.CreateClient().PostAsJsonAsync(
            LOCAL_PATH,
            new LocalSessionRequestDto { DisplayName = displayName, JoinCode = joinCode });
    }

    private static async Task<string?> ErrorOf(HttpResponseMessage response)
    {
        ApiErrorDto? error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        return error?.Error;
    }
}
