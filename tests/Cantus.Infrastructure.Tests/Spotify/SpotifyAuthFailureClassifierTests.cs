using System.Net;
using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public sealed class SpotifyAuthFailureClassifierTests
{
    [Theory]
    [InlineData("access_denied", SpotifyAuthFailure.Cancelled)]
    [InlineData("ACCESS_DENIED", SpotifyAuthFailure.Cancelled)]
    [InlineData("server_error", SpotifyAuthFailure.Unknown)]
    [InlineData(null, SpotifyAuthFailure.Unknown)]
    public void FromCallbackError_MapsSpotifyErrorValues(string? error, SpotifyAuthFailure expected)
    {
        SpotifyAuthFailureClassifier.FromCallbackError(error).Should().Be(expected);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"status\":403,\"message\":\"User not registered in the Developer Dashboard\"}}", SpotifyAuthFailure.NotAllowlisted)]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"status\":403,\"message\":\"Active premium subscription required for the owner of the app.\"}}", SpotifyAuthFailure.OwnerNeedsPremium)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\",\"error_description\":\"Invalid client\"}", SpotifyAuthFailure.InvalidClientId)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"Invalid authorization code\"}", SpotifyAuthFailure.InvalidGrant)]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"status\":403,\"message\":\"Forbidden\"}}", SpotifyAuthFailure.NotAllowlisted)]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":{\"status\":500,\"message\":\"Server error\"}}", SpotifyAuthFailure.Unknown)]
    public void FromException_MapsSpotifyApiErrors(HttpStatusCode status, string body, SpotifyAuthFailure expected)
    {
        APIException exception = new(new FakeResponse(status, body));

        SpotifyAuthFailureClassifier.FromException(exception).Should().Be(expected);
    }

    [Fact]
    public void FromException_NotConfigured_IsNotConfigured()
    {
        SpotifyAuthFailureClassifier.FromException(new SpotifyNotConfiguredException())
            .Should().Be(SpotifyAuthFailure.NotConfigured);
    }

    [Fact]
    public void FromException_UnrelatedException_IsUnknown()
    {
        SpotifyAuthFailureClassifier.FromException(new TimeoutException("premium"))
            .Should().Be(SpotifyAuthFailure.Unknown);
    }

    private sealed class FakeResponse : IResponse
    {
        public FakeResponse(HttpStatusCode statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        public object? Body { get; }

        public IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        public HttpStatusCode StatusCode { get; }

        public string? ContentType => "application/json";
    }
}
