using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Server.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Middleware;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_WhenExceptionOccurs_ReturnsProblemDetailsAndLogsError()
    {
        // Arrange
        Mock<ILogger<GlobalExceptionHandler>> mockLogger = new();
        Mock<IHostEnvironment> mockEnv = new();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        GlobalExceptionHandler handler = new(mockLogger.Object, mockEnv.Object);

        DefaultHttpContext httpContext = new();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/test/fail";
        httpContext.TraceIdentifier = "test-trace-12345";
        httpContext.Response.Body = new MemoryStream();

        InvalidOperationException testException = new("Database connection failed unexpectedly");

        // Act
        bool result = await handler.TryHandleAsync(httpContext, testException, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        httpContext.Response.ContentType.Should().Contain("application/problem+json");

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(httpContext.Response.Body);
        string responseJson = await reader.ReadToEndAsync();

        ProblemDetails? problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseJson);
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(500);
        problemDetails.Instance.Should().Be("/api/test/fail");

        // Verify that LogError was called
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                testException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
