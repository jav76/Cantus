using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Server.Services;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class CredentialResolverTests : IDisposable
{
    private const string SESSION_COOKIE = "cantus_session_id";
    private const string DISPLAY_COOKIE = "cantus_display_token";

    private readonly InMemorySessionStore _store = new();

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task ResolveAsync_WithSessionCookie_ReturnsSessionCredential()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");

        CallerCredential? caller = await Resolve(WithCookie(SESSION_COOKIE, owner.Id));

        caller.Should().NotBeNull();
        caller!.Kind.Should().Be(CredentialKind.Session);
        caller.Owner.Id.Should().Be(owner.Id);
        caller.Display.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WithDisplayCookie_ReturnsDisplayCredentialForOwner()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay created = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");

        CallerCredential? caller = await Resolve(WithCookie(DISPLAY_COOKIE, created.Token));

        AssertDisplay(caller, owner, created);
    }

    [Theory]
    [InlineData("display_token")]
    [InlineData("access_token")]
    public async Task ResolveAsync_WithDisplayTokenInQuery_ReturnsDisplayCredential(string parameter)
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay created = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");

        CallerCredential? caller = await Resolve(WithQuery(parameter, created.Token));

        AssertDisplay(caller, owner, created);
    }

    [Fact]
    public async Task ResolveAsync_WithDisplayTokenAsBearer_ReturnsDisplayCredential()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay created = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers.Authorization = $"Bearer {created.Token}";

        CallerCredential? caller = await Resolve(httpContext);

        AssertDisplay(caller, owner, created);
    }

    [Fact]
    public async Task ResolveAsync_WithDisplayTokenInSessionCookie_IsNotASession()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay created = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");

        CallerCredential? caller = await Resolve(WithCookie(SESSION_COOKIE, created.Token));

        caller!.Kind.Should().Be(CredentialKind.Display);
    }

    [Fact]
    public async Task ResolveAsync_WithSessionAndDisplayCredentials_PrefersSession()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay created = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers.Cookie = $"{SESSION_COOKIE}={owner.Id}; {DISPLAY_COOKIE}={created.Token}";

        CallerCredential? caller = await Resolve(httpContext);

        caller!.Kind.Should().Be(CredentialKind.Session);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("token")]
    [InlineData("Authorization")]
    public async Task ResolveAsync_WithStaleSessionCookieAndDisplayTokenInSharedSlot_ReturnsDisplayCredential(
        string slot)
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay created = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers.Cookie = $"{SESSION_COOKIE}=revoked-session-id";
        if (slot == "Authorization")
        {
            httpContext.Request.Headers.Authorization = $"Bearer {created.Token}";
        }
        else
        {
            httpContext.Request.QueryString = QueryString.Create(slot, created.Token);
        }

        CallerCredential? caller = await Resolve(httpContext);

        AssertDisplay(caller, owner, created);
    }

    [Fact]
    public async Task ResolveAsync_WithRevokedDisplayToken_ReturnsNull()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay created = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        await _store.CreateDisplayService().RevokeAsync(owner.Id, created.Display.Id);

        CallerCredential? caller = await Resolve(WithCookie(DISPLAY_COOKIE, created.Token));

        caller.Should().BeNull();
    }

    [Theory]
    [InlineData(SESSION_COOKIE)]
    [InlineData(DISPLAY_COOKIE)]
    public async Task ResolveAsync_WithSpotifyUserId_ReturnsNull(string cookie)
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");

        CallerCredential? caller = await Resolve(WithCookie(cookie, owner.RequiredSpotifyUserId()));

        caller.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WithNoCredential_ReturnsNull()
    {
        CallerCredential? caller = await Resolve(new DefaultHttpContext());

        caller.Should().BeNull();
    }

    private Task<CallerCredential?> Resolve(HttpContext httpContext)
    {
        return _store.CreateCredentialResolver().ResolveAsync(httpContext);
    }

    private static void AssertDisplay(CallerCredential? caller, UserSessionEntity owner, CreatedDisplay created)
    {
        caller.Should().NotBeNull();
        caller!.Kind.Should().Be(CredentialKind.Display);
        caller.IsSession.Should().BeFalse();
        caller.Owner.Id.Should().Be(owner.Id);
        caller.Display!.Id.Should().Be(created.Display.Id);
    }

    private static HttpContext WithCookie(string cookie, string value)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers.Cookie = $"{cookie}={value}";
        return httpContext;
    }

    private static HttpContext WithQuery(string parameter, string value)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.QueryString = QueryString.Create(parameter, value);
        return httpContext;
    }
}
