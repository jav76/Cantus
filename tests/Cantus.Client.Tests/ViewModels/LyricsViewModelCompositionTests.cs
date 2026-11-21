using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelCompositionTests
{
    private const BindingFlags INSTANCE_MEMBERS =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void Root_ExposesEachExtractedViewModel()
    {
        // Arrange & Act
        LyricsViewModel vm = new(new SignalRPlaybackClient());

        // Assert
        vm.Transport.Should().NotBeNull();
        vm.Timing.Should().NotBeNull();
        vm.Session.Should().NotBeNull();
        vm.Lookup.Should().NotBeNull();
        vm.Theme.Should().NotBeNull();
        vm.Layout.Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(PlaybackTransport))]
    [InlineData(typeof(PlaybackTiming))]
    [InlineData(typeof(SessionViewModel))]
    [InlineData(typeof(SongLookupViewModel))]
    [InlineData(typeof(ThemeManager))]
    [InlineData(typeof(ResponsiveLayoutManager))]
    public void Root_DoesNotForwardMembersOfAComposedType(Type extracted)
    {
        // Arrange
        HashSet<string> rootMembers = VisibleMemberSignatures(typeof(LyricsViewModel));

        // Act
        List<string> forwarded = VisibleMemberSignatures(extracted)
            .Where(rootMembers.Contains)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

        // Assert
        forwarded.Should().BeEmpty("views bind to {0} directly", extracted.Name);
    }

    [Theory]
    [InlineData("ServerBaseUrl")]
    [InlineData("ClientId")]
    public void Root_DoesNotForwardClientConnectionDetails(string member)
    {
        // Act
        MemberInfo[] members = typeof(LyricsViewModel).GetMember(member, INSTANCE_MEMBERS);

        // Assert
        members.Should().BeEmpty("the Spotify sign-in link comes from {0}", nameof(SessionViewModel));
    }

    private static HashSet<string> VisibleMemberSignatures(Type type)
    {
        IEnumerable<string> properties = type
            .GetProperties(INSTANCE_MEMBERS)
            .Where(property => property.GetAccessors(nonPublic: true).Any(IsVisible))
            .Select(property => property.Name);

        IEnumerable<string> methods = type
            .GetMethods(INSTANCE_MEMBERS)
            .Where(method => !method.IsSpecialName && IsVisible(method))
            .Select(method =>
                $"{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName))})");

        return properties.Concat(methods).ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsVisible(MethodInfo method) => method.IsPublic || method.IsAssembly;
}
