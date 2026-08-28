using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Cantus.Core.Logging;
using Cantus.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Cantus.Core.Tests.Logging;

public class TraceLogDecoratorGeneratorTests
{
    [Fact]
    public void Generator_WhenInterfaceHasTraceLogAttribute_GeneratesDecoratorWithTracingAndRedaction()
    {
        // Arrange
        string sourceCode = """
        using System;
        using System.Threading.Tasks;
        using Cantus.Core.Logging;

        namespace Cantus.TestServices;

        [TraceLog]
        public interface IPlaybackService
        {
            Task<bool> PlayTrackAsync(string trackId, [Redact] string authToken);
            void Stop();
        }
        """;

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TraceLogAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        TraceLogDecoratorGenerator generator = new();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        // Act
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        // Assert
        diagnostics.Should().BeEmpty();
        runResult.GeneratedTrees.Length.Should().Be(1);

        string generatedCode = runResult.GeneratedTrees[0].ToString();
        generatedCode.Should().Contain("public sealed class TraceLoggingPlaybackServiceDecorator : global::Cantus.TestServices.IPlaybackService");
        generatedCode.Should().Contain("authToken=[REDACTED]");
        generatedCode.Should().Contain("trackId={trackId}");
        generatedCode.Should().Contain("Entering IPlaybackService.PlayTrackAsync");
        generatedCode.Should().Contain("Exiting IPlaybackService.PlayTrackAsync");
        generatedCode.Should().Contain("Entering IPlaybackService.Stop");
        generatedCode.Should().Contain("AddTraceDecorated<TImpl>");
    }
}
