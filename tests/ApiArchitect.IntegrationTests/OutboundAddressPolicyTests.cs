using System.Net;
using ApiArchitect.Api.Features.Execution;

namespace ApiArchitect.IntegrationTests;

public sealed class OutboundAddressPolicyTests
{
    private static readonly OutboundAddressPolicy DefaultPolicy = new(allowPrivateNetworks: false, []);
    private static readonly OutboundAddressPolicy LocalPolicy = new(allowPrivateNetworks: true, []);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("168.63.129.16")] // Azure WireServer
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64 to 169.254.169.254
    [InlineData("2002:7f00:1::")] // 6to4 wrapping 127.0.0.1
    [InlineData("ff02::1")]
    public void Internal_addresses_are_blocked(string address)
    {
        Assert.False(DefaultPolicy.IsAllowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")] // just outside 172.16.0.0/12
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string address)
    {
        Assert.True(DefaultPolicy.IsAllowed(IPAddress.Parse(address)));
    }

    [Fact]
    public void Allowed_networks_override_the_block_for_their_range_only()
    {
        var policy = new OutboundAddressPolicy(allowPrivateNetworks: false, [IPNetwork.Parse("10.1.0.0/16")]);

        Assert.True(policy.IsAllowed(IPAddress.Parse("10.1.2.3")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("10.2.0.1")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("127.0.0.1")));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.168.1.20")]
    [InlineData("172.17.0.1")] // Docker bridge gateway
    [InlineData("10.0.0.5")]
    public void Local_mode_allows_loopback_and_private_networks(string address)
    {
        Assert.True(LocalPolicy.IsAllowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("168.63.129.16")]
    [InlineData("fd00:ec2::254")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    public void Metadata_and_reserved_addresses_are_blocked_even_in_local_mode(string address)
    {
        Assert.False(LocalPolicy.IsAllowed(IPAddress.Parse(address)));
        Assert.False(new OutboundAddressPolicy(true, [IPNetwork.Parse("0.0.0.0/0")]).IsAllowed(IPAddress.Parse(address)));
    }
}
