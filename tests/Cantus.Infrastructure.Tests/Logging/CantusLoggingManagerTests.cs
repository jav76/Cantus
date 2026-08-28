using System;
using System.IO;
using Cantus.Core.Logging;
using Cantus.Infrastructure.Logging;
using FluentAssertions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Logging;

public class CantusLoggingManagerTests
{
    [Theory]
    [InlineData("none", LoggingConfiguration.None)]
    [InlineData("None", LoggingConfiguration.None)]
    [InlineData("debug", LoggingConfiguration.Debug)]
    [InlineData("Debug", LoggingConfiguration.Debug)]
    [InlineData("trace", LoggingConfiguration.Trace)]
    [InlineData("Trace", LoggingConfiguration.Trace)]
    [InlineData("", LoggingConfiguration.None)]
    [InlineData(null, LoggingConfiguration.None)]
    [InlineData("invalid", LoggingConfiguration.None)]
    public void ParseConfiguration_ShouldParseCorrectly(string? input, LoggingConfiguration expected)
    {
        // Act
        LoggingConfiguration result = CantusLoggingManager.ParseConfiguration(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void InitializeServer_WithDebugConfig_CreatesLogDirectoryAndConfiguresLogManager()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "cantus_test_logs_" + Guid.NewGuid().ToString("N"));

        try
        {
            // Act
            CantusLoggingManager.InitializeServer(
                LoggingConfiguration.Debug,
                dbConnectionString: "Data Source=test.db",
                logDirectory: tempDir);

            // Assert
            CantusLoggingManager.CurrentConfiguration.Should().Be(LoggingConfiguration.Debug);
            CantusLoggingManager.IsInitialized.Should().BeTrue();
            Directory.Exists(tempDir).Should().BeTrue();
        }
        finally
        {
            CantusLoggingManager.Shutdown();
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                }
            }
        }
    }
}
