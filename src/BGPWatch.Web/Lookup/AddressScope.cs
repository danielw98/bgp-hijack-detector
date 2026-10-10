using System.Net;
using BGPWatch.Core.Domain;

namespace BGPWatch.Web.Lookup;

/// <summary>Whether an address is reachable on the public Internet (the IANA special-purpose registries).</summary>
public static class AddressScope
{
    private static readonly IPNetwork[] SpecialPurpose =
    [
        .. new[]
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
            "192.0.0.0/24", "192.0.2.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24",
            "203.0.113.0/24", "240.0.0.0/4", "255.255.255.255/32",
            "::/128", "::1/128", "::ffff:0:0/96", "64:ff9b:1::/48", "100::/64", "2001::/23", "2001:db8::/32",
            "2002::/16", "fc00::/7", "fe80::/10",
        }.Select(IPNetwork.Parse),
    ];

    public static bool IsGlobal(IPAddress address)
    {
        var host = new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
        return !SpecialPurpose.Any(n => n.Covers(host));
    }
}
