using System.Net;
using System.Net.Sockets;

namespace Cantus.Server.Services;

/// <summary>
/// Turns a client address into a rate-limit partition key. IPv6 clients usually hold a whole
/// /64, so every address in one /64 shares a key; otherwise one client could rotate source
/// addresses to get a fresh limit each time.
/// </summary>
public static class ClientAddressPartition
{
    public const string UNKNOWN_ADDRESS = "unknown";

    private const int IPV6_PREFIX_LENGTH_BITS = 64;
    private const int BITS_PER_BYTE = 8;

    public static string GetKey(IPAddress? address)
    {
        if (address is null)
        {
            return UNKNOWN_ADDRESS;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        byte[] bytes = address.GetAddressBytes();
        int prefixBytes = IPV6_PREFIX_LENGTH_BITS / BITS_PER_BYTE;
        Array.Clear(bytes, prefixBytes, bytes.Length - prefixBytes);
        return $"{new IPAddress(bytes)}/{IPV6_PREFIX_LENGTH_BITS}";
    }
}
