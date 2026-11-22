using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

/// <summary>Sign-in with the user's own Spotify app, the setup info endpoint and the failure pages.</summary>
public sealed class SpotifyOwnAppEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SERVER_CLIENT_ID = "0123456789abcdef0123456789abcdef";
    private const string OWN_CLIENT_ID = "fedcba9876543210fedcba9876543210";
    private const string USER_CLIENT_ID_COOKIE = "cantus_spotify_client_id";

    private readonly WebApplicationFactory<Program> _baseFactory;
    private readonly Mock<ISpotifyAuthService> _mockAuthService = new();

    public SpotifyOwnAppEndpointTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
        _mockAuthService
            .Setup(a => a.GetAuthorizationUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new Uri("https://accounts.spotify.com/authorize?test=1"));
    }

    [Fact]
    public async Task Login_WithOwnClientId_PassesItToSpotifyAndKeepsItForTheCallback()
    {
        HttpClient client = CreateClient(serverClientId: null, allowUserClientIds: null);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/auth/spotify/login?json=true&spotify_client_id={OWN_CLIENT_ID.ToUpperInvariant()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockAuthService.Verify(a => a.GetAuthorizationUri(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), OWN_CLIENT_ID));
        SetCookies(response).Should().Contain(c => c.StartsWith($"{USER_CLIENT_ID_COOKIE}={OWN_CLIENT_ID}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WithoutOwnClientId_ClearsAnyEarlierOwnClientIdCookie()
    {
        HttpClient client = CreateClient(SERVER_CLIENT_ID, allowUserClientIds: null);

        HttpResponseMessage response = await client.GetAsync("/api/auth/spotify/login?json=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockAuthService.Verify(a => a.GetAuthorizationUri(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), null));
        SetCookies(response).Should().Contain(c => c.StartsWith($"{USER_CLIENT_ID_COOKIE}=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WithMalformedOwnClientId_ShowsTheInvalidClientIdPage()
    {
        HttpClient client = CreateClient(serverClientId: null, allowUserClientIds: null);

        HttpResponseMessage response = await client.GetAsync("/api/auth/spotify/login?spotify_client_id=not-a-client-id");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Spotify did not accept this Client ID");
        _mockAuthService.Verify(
            a => a.GetAuthorizationUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Theory]
    [InlineData(SERVER_CLIENT_ID, null)]
    [InlineData(SERVER_CLIENT_ID, false)]
    [InlineData(null, false)]
    public async Task Login_WithOwnClientIdWhenNotAllowed_IsForbidden(string? serverClientId, bool? allowUserClientIds)
    {
        HttpClient client = CreateClient(serverClientId, allowUserClientIds);

        HttpResponseMessage response = await client.GetAsync($"/api/auth/spotify/login?spotify_client_id={OWN_CLIENT_ID}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Own Spotify apps are turned off here");
    }

    [Fact]
    public async Task Login_WhenNoClientIdAnywhere_ShowsTheNotConfiguredPageWithTheSetupLink()
    {
        _mockAuthService
            .Setup(a => a.GetAuthorizationUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws<SpotifyNotConfiguredException>();
        HttpClient client = CreateClient(serverClientId: null, allowUserClientIds: null);

        HttpResponseMessage response = await client.GetAsync("/api/auth/spotify/login");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        string html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Spotify is not set up on this server");
        html.Should().Contain("href=\"/?connect=spotify\"");
    }

    [Fact]
    public async Task Callback_WithOwnClientIdCookie_ExchangesWithItAndClearsTheCookie()
    {
        _mockAuthService
            .Setup(a => a.ExchangeCodeAsync("code-1", "verifier-1", It.IsAny<string>(), OWN_CLIENT_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Session());
        HttpClient client = CreateClient(serverClientId: null, allowUserClientIds: null);

        HttpResponseMessage response = await SendCallbackAsync(
            client,
            $"cantus_oauth_state=state-1; cantus_pkce_verifier=verifier-1; {USER_CLIENT_ID_COOKIE}={OWN_CLIENT_ID}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockAuthService.Verify(a => a.ExchangeCodeAsync(
            "code-1", "verifier-1", It.IsAny<string>(), OWN_CLIENT_ID, It.IsAny<CancellationToken>()));
        SetCookies(response).Should().Contain(c => c.StartsWith($"{USER_CLIENT_ID_COOKIE}=;", StringComparison.Ordinal));
        string html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("href=\"/\"").And.NotContain("auth=success");
    }

    [Fact]
    public async Task Callback_WhenSpotifyRejectsTheAccount_ShowsTheAllowlistPage()
    {
        _mockAuthService
            .Setup(a => a.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpotifyAPI.Web.APIException("User not registered in the Developer Dashboard"));
        HttpClient client = CreateClient(SERVER_CLIENT_ID, allowUserClientIds: true);

        HttpResponseMessage response = await SendCallbackAsync(client, "cantus_oauth_state=state-1; cantus_pkce_verifier=verifier-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Your account is not on this app&#39;s user list");
        html.Should().Contain("Set up your own Spotify app");
    }

    [Fact]
    public async Task Callback_WhenOwnAppsAreOff_DoesNotOfferTheSetupLink()
    {
        _mockAuthService
            .Setup(a => a.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SpotifyAPI.Web.APIException("User not registered in the Developer Dashboard"));
        HttpClient client = CreateClient(SERVER_CLIENT_ID, allowUserClientIds: null);

        HttpResponseMessage response = await SendCallbackAsync(client, "cantus_oauth_state=state-1; cantus_pkce_verifier=verifier-1");

        (await response.Content.ReadAsStringAsync()).Should().NotContain("connect=spotify");
    }

    [Fact]
    public async Task Callback_WhenUserCancels_ShowsTheCancelledPage()
    {
        HttpClient client = CreateClient(SERVER_CLIENT_ID, allowUserClientIds: null);

        HttpResponseMessage response = await client.GetAsync("/api/auth/spotify/callback?error=access_denied&state=state-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Sign-in cancelled");
    }

    [Fact]
    public async Task Callback_WithoutTheStateCookie_ShowsTheExpiredPage()
    {
        HttpClient client = CreateClient(SERVER_CLIENT_ID, allowUserClientIds: null);

        HttpResponseMessage response = await client.GetAsync("/api/auth/spotify/callback?code=code-1&state=state-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Sign-in expired");
    }

    [Theory]
    [InlineData("127.0.0.1:5000", "http://127.0.0.1:5000/api/auth/spotify/callback", true)]
    [InlineData("localhost:5000", "http://localhost:5000/api/auth/spotify/callback", false)]
    [InlineData("192.168.1.20:5000", "http://192.168.1.20:5000/api/auth/spotify/callback", false)]
    [InlineData("[::1]:5000", "http://[::1]:5000/api/auth/spotify/callback", true)]
    public async Task SetupInfo_ReportsTheRedirectUriAndWhetherSpotifyAcceptsIt(string host, string redirectUri, bool accepted)
    {
        HttpClient client = CreateClient(serverClientId: null, allowUserClientIds: null);
        HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/spotify/setup");
        request.Headers.Host = host;

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        SpotifySetupInfoDto? info = await response.Content.ReadFromJsonAsync<SpotifySetupInfoDto>();
        info!.RedirectUri.Should().Be(redirectUri);
        info.RedirectUriAccepted.Should().Be(accepted);
        info.ServerClientIdConfigured.Should().BeFalse();
        info.UserClientIdsAllowed.Should().BeTrue();
        info.DashboardUrl.Should().StartWith("https://developer.spotify.com/");
    }

    [Fact]
    public async Task SetupInfo_ForAnHttpsHost_IsAccepted()
    {
        HttpClient client = CreateClient(SERVER_CLIENT_ID, allowUserClientIds: null, hostUrl: "https://cantus.example.com");

        // A non-loopback Host, so CANTUS_HOST_URL wins over the request address.
        HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/spotify/setup");
        request.Headers.Host = "cantus.internal:8080";

        HttpResponseMessage response = await client.SendAsync(request);

        SpotifySetupInfoDto? info = await response.Content.ReadFromJsonAsync<SpotifySetupInfoDto>();
        info!.RedirectUri.Should().Be("https://cantus.example.com/api/auth/spotify/callback");
        info.RedirectUriAccepted.Should().BeTrue();
        info.ServerClientIdConfigured.Should().BeTrue();
        info.UserClientIdsAllowed.Should().BeFalse();
    }

    private HttpClient CreateClient(string? serverClientId, bool? allowUserClientIds, string? hostUrl = null)
    {
        WebApplicationFactory<Program> factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Spotify:ClientId", serverClientId ?? string.Empty);
            builder.UseSetting("Spotify:AllowUserClientIds", allowUserClientIds?.ToString() ?? string.Empty);
            builder.UseSetting("CANTUS_HOST_URL", hostUrl ?? string.Empty);
            builder.ConfigureTestServices(services => services.AddScoped(_ => _mockAuthService.Object));
        });
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static Task<HttpResponseMessage> SendCallbackAsync(HttpClient client, string cookies)
    {
        HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/spotify/callback?code=code-1&state=state-1");
        request.Headers.Add("Cookie", cookies);
        return client.SendAsync(request);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values : Array.Empty<string>();

    private static UserSession Session() => new()
    {
        Id = "sess-own",
        SpotifyUserId = "sp-own",
        DisplayName = "Own App User",
        AccessToken = "token",
        RefreshToken = "refresh"
    };
}
