using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Cantus.Core.Tests.Logging;

/// <summary>
/// Guards against logging a Session id, a Display token or another credential. A Session id
/// is the session cookie value, so it may only reach a log through <see cref="SessionLogRef"/>.
/// The check is syntactic: it flags log call arguments whose names read as a credential.
/// </summary>
public sealed partial class LogCredentialGuardTests
{
    private static readonly HashSet<string> LogMethods =
    [
        "Log", "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical"
    ];

    private static readonly HashSet<string> AllowedTokenNames =
    [
        "cancellationToken", "stoppingToken", "CancellationToken"
    ];

    [GeneratedRegex(@"\{(UserId|SessionId|OwnerSessionId|\w*Token)(:[^}]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenPlaceholder();

    [GeneratedRegex(@"^(userId|sessionId|\w*SessionId|uid|trackedId)$", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenIdentifier();

    [GeneratedRegex(@"token$", RegexOptions.IgnoreCase)]
    private static partial Regex TokenIdentifier();

    [GeneratedRegex(@"session|^Owner$", RegexOptions.IgnoreCase)]
    private static partial Regex SessionReceiver();

    [Fact]
    public void SourceLogCalls_NeverPassASessionIdOrToken()
    {
        string srcRoot = Path.Combine(FindRepoRoot(), "src");
        List<string> violations = [];

        foreach (string file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(srcRoot, file);
            if (IsGeneratedOrBuildOutput(relative))
            {
                continue;
            }

            violations.AddRange(FindViolations(File.ReadAllText(file))
                .Select(v => $"{relative}: {v}"));
        }

        violations.Should().BeEmpty(
            "a Session id or token must never be logged (wrap a Session id in SessionLogRef.For), but found:\n{0}",
            string.Join("\n", violations));
    }

    [Theory]
    [InlineData("""_logger.LogInformation("Command by {UserId}", userId);""")]
    [InlineData("""_logger.LogWarning("Expired for {SessionRef}", session.Id);""")]
    [InlineData("""_logger.LogError(ex, "Failed for {Id}", sessionEntity.Id);""")]
    [InlineData("""_logger.LogInformation("Connected {Owner}", caller.Owner.Id);""")]
    [InlineData("""_logger.LogDebug("Paired with {Value}", displayToken);""")]
    [InlineData("""_logger.LogDebug("Calling with {Value}", session.AccessToken);""")]
    [InlineData("""logger.LogInformation($"Session {sessionId} connected");""")]
    [InlineData("""_logger.LogInformation("Ref {SessionRef} id {SessionId}", SessionLogRef.For(id), id);""")]
    public void FindViolations_FlagsCredentialArguments(string statement)
    {
        FindViolations(Wrap(statement)).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("""_logger.LogInformation("Command by {SessionRef}", SessionLogRef.For(userId));""")]
    [InlineData("""_logger.LogWarning("Expired for {SpotifyUserId}", session.SpotifyUserId);""")]
    [InlineData("""_logger.LogInformation("Display {DisplayId} created", entity.Id);""")]
    [InlineData("""_logger.LogInformation("Track {TrackId}", trackId);""")]
    [InlineData("""await Task.Delay(1000, stoppingToken);""")]
    public void FindViolations_AllowsSafeArguments(string statement)
    {
        FindViolations(Wrap(statement)).Should().BeEmpty();
    }

    /// <summary>
    /// The [TraceLog] decorators log every argument and return value at Trace level, so a
    /// credential parameter on a traced interface must be marked [Redact].
    /// </summary>
    [Fact]
    public void TracedInterfaces_RedactEveryCredentialParameter()
    {
        IEnumerable<string> unredacted = typeof(ISpotifyAuthService).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.GetCustomAttribute<TraceLogAttribute>() is not null)
            .SelectMany(t => t.GetMethods().Select(m => (Type: t, Method: m)))
            .SelectMany(x => x.Method.GetParameters().Select(p => (x.Type, x.Method, Parameter: p)))
            .Where(x => x.Parameter.ParameterType != typeof(CancellationToken)
                && IsCredentialParameterName(x.Parameter.Name!)
                && x.Parameter.GetCustomAttribute<RedactAttribute>() is null)
            .Select(x => $"{x.Type.Name}.{x.Method.Name}({x.Parameter.Name})");

        List<string> found = unredacted.ToList();
        found.Should().BeEmpty("credential parameters must be [Redact], but found: {0}", string.Join(", ", found));
    }

    private static bool IsCredentialParameterName(string name) =>
        ForbiddenIdentifier().IsMatch(name)
        || TokenIdentifier().IsMatch(name)
        || name is "code" or "codeVerifier";

    private static string Wrap(string statement) => $$"""
        class C
        {
            async void M()
            {
                {{statement}}
            }
        }
        """;

    private static IEnumerable<string> FindViolations(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member
                || !LogMethods.Contains(member.Name.Identifier.Text))
            {
                continue;
            }

            int line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                foreach (string reason in InspectArgument(argument.Expression))
                {
                    yield return $"line {line}: {reason}";
                }
            }
        }
    }

    private static IEnumerable<string> InspectArgument(ExpressionSyntax expression)
    {
        foreach (LiteralExpressionSyntax literal in expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
        {
            Match match = ForbiddenPlaceholder().Match(literal.Token.ValueText);
            if (match.Success)
            {
                yield return $"placeholder {match.Value}";
            }
        }

        IEnumerable<SyntaxNode> nodes = expression.DescendantNodesAndSelf(node => !IsSessionLogRefCall(node));

        foreach (SyntaxNode node in nodes)
        {
            if (IsSessionLogRefCall(node))
            {
                continue;
            }

            switch (node)
            {
                case MemberAccessExpressionSyntax access
                    when access.Name.Identifier.Text == "Id"
                        && access.Expression is IdentifierNameSyntax or MemberAccessExpressionSyntax
                        && SessionReceiver().IsMatch(LastName(access.Expression)):
                    yield return $"session id {access}";
                    break;
                case IdentifierNameSyntax identifier
                    when ForbiddenIdentifier().IsMatch(identifier.Identifier.Text):
                    yield return $"identifier {identifier.Identifier.Text}";
                    break;
                case IdentifierNameSyntax identifier
                    when TokenIdentifier().IsMatch(identifier.Identifier.Text)
                        && !AllowedTokenNames.Contains(identifier.Identifier.Text):
                    yield return $"token {identifier.Identifier.Text}";
                    break;
            }
        }
    }

    private static string LastName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        _ => string.Empty
    };

    private static bool IsSessionLogRefCall(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.Text: nameof(SessionLogRef) },
                Name.Identifier.Text: nameof(SessionLogRef.For)
            }
        };

    private static bool IsGeneratedOrBuildOutput(string relativePath)
    {
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "obj" or "bin");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cantus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find Cantus.slnx above the test output directory.");
    }
}
