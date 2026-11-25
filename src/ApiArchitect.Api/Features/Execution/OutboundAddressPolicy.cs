using System.Net;
using System.Net.Sockets;

namespace ApiArchitect.Api.Features.Execution;

/// <summary>
/// Server-side request forgery (SSRF) protection. Users choose the URLs this server calls, so without this they
/// could reach the server's own loopback interface, the private network around it, or the cloud metadata
/// service (169.254.169.254) that hands out credentials.
/// </summary>
/// <remarks>
/// The check runs in <see cref="ConnectAsync"/>, which <see cref="SocketsHttpHandler.ConnectCallback"/> calls for
/// every new connection. It resolves the host, checks every address, and connects to the checked address itself.
/// Checking the URL up front instead would be bypassed by DNS rebinding: a hostname that resolves to a public
/// address during validation and to 127.0.0.1 a moment later when the connection opens.
/// </remarks>
public sealed class OutboundAddressPolicy(IEnumerable<IPNetwork> allowedNetworks)
{
    private readonly IPNetwork[] _allowedNetworks = [.. allowedNetworks];

    /// <summary>
    /// Ranges that are not the public internet: loopback, private (RFC 1918), carrier-grade NAT, link-local
    /// (including cloud metadata), multicast, reserved and documentation ranges, and IPv6 transition prefixes
    /// that embed an IPv4 address (NAT64, 6to4, Teredo) and could tunnel to any of those.
    /// </summary>
    private static readonly IPNetwork[] BlockedNetworks =
    [
        // IPv4
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.88.99.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        // Azure's platform endpoint (WireServer). Public-looking, but only reachable from inside Azure.
        IPNetwork.Parse("168.63.129.16/32"),

        // IPv6
        IPNetwork.Parse("::/96"), // unspecified, loopback and IPv4-compatible
        IPNetwork.Parse("64:ff9b::/96"),
        IPNetwork.Parse("64:ff9b:1::/48"),
        IPNetwork.Parse("100::/64"),
        IPNetwork.Parse("2001::/32"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("fec0::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    public bool IsAllowed(IPAddress address)
    {
        // ::ffff:10.0.0.1 is 10.0.0.1. Check the IPv4 form so mapped addresses can't slip past the IPv4 ranges.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        foreach (var network in _allowedNetworks)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        foreach (var network in BlockedNetworks)
        {
            if (network.Contains(address))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Resolves the host, refuses if any address is internal, and opens the socket to a checked address.</summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);

        // Refuse if any record is internal, not just the first: a host with one public and one private record
        // would otherwise depend on which one the connection happens to try.
        if (addresses.Length == 0 || !addresses.All(IsAllowed))
        {
            throw new BlockedAddressException(endPoint.Host);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // Connects to these exact addresses. Nothing resolves the name again after the check.
            await socket.ConnectAsync(addresses, endPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>Thrown from the connect callback when a host resolves to an address the policy refuses.</summary>
public sealed class BlockedAddressException(string host)
    : Exception($"'{host}' resolves to an address that requests are not allowed to reach.");
