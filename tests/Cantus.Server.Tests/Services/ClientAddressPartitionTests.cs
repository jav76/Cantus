using System.Net;
using Cantus.Server.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class ClientAddressPartitionTests
{
    [Fact]
    public void GetKey_WithoutAddress_ReturnsUnknown()
    {
        ClientAddressPartition.GetKey(null).Should().Be(ClientAddressPartition.UNKNOWN_ADDRESS);
    }

    [Fact]
    public void GetKey_ForIpv4_ReturnsTheAddress()
    {
        ClientAddressPartition.GetKey(IPAddress.Parse("203.0.113.7")).Should().Be("203.0.113.7");
    }

    [Fact]
    public void GetKey_ForIpv4MappedIpv6_ReturnsTheIpv4Address()
    {
        ClientAddressPartition.GetKey(IPAddress.Parse("::ffff:203.0.113.7")).Should().Be("203.0.113.7");
    }

    [Theory]
    [InlineData("2001:db8:1:2::1")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff")]
    public void GetKey_ForIpv6InOneSlash64_ReturnsTheSharedPrefix(string address)
    {
        ClientAddressPartition.GetKey(IPAddress.Parse(address)).Should().Be("2001:db8:1:2::/64");
    }

    [Fact]
    public void GetKey_ForIpv6InDifferentSlash64s_ReturnsDifferentKeys()
    {
        string first = ClientAddressPartition.GetKey(IPAddress.Parse("2001:db8:1:2::1"));
        string second = ClientAddressPartition.GetKey(IPAddress.Parse("2001:db8:1:3::1"));

        first.Should().NotBe(second);
    }
}
