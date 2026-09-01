using System.Collections.Generic;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class SessionTokenResolverTests
{
    private readonly SessionTokenResolver _resolver = new();

    [Fact]
    public void ResolveSessionId_WhenContextIsNull_ReturnsNull()
    {
        string? result = _resolver.ResolveSessionId(null);
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSessionId_WhenQueryContainsToken_ReturnsToken()
    {
        DefaultHttpContext context = new();
        Dictionary<string, StringValues> query = new()
        {
            ["token"] = "my_query_token"
        };
        context.Request.Query = new QueryCollection(query);

        string? result = _resolver.ResolveSessionId(context);
        result.Should().Be("my_query_token");
    }

    [Fact]
    public void ResolveSessionId_WhenQueryContainsSessionId_ReturnsSessionId()
    {
        DefaultHttpContext context = new();
        Dictionary<string, StringValues> query = new()
        {
            ["session_id"] = "session_123"
        };
        context.Request.Query = new QueryCollection(query);

        string? result = _resolver.ResolveSessionId(context);
        result.Should().Be("session_123");
    }

    [Fact]
    public void ResolveSessionId_WhenAuthorizationHeaderBearerPresent_ReturnsExtractedToken()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "Bearer bearer_secret_token";

        string? result = _resolver.ResolveSessionId(context);
        result.Should().Be("bearer_secret_token");
    }

    [Fact]
    public void ResolveSessionId_WhenAuthorizationHeaderWithoutBearer_ReturnsRawHeader()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "raw_token_value";

        string? result = _resolver.ResolveSessionId(context);
        result.Should().Be("raw_token_value");
    }
}
