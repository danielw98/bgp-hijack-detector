using BGPWatch.Web.Bmp;
using static BGPWatch.Tests.Wire;

namespace BGPWatch.Tests;

/// <summary>
/// BMP streams captured from the FRR 10.4.5 lab on 2026-10-10, one per router, over a
/// baseline, the "both" hijack scenario and its reset (Loc-RIB monitoring, RFC 9069).
/// </summary>
public class BmpTests
{
    private static readonly DateTime Arrival = new(2026, 10, 10, 1, 5, 35, DateTimeKind.Utc);

    private static async Task<List<BmpMessage>> MessagesAsync(string router)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"bmp-{router}.bin"));
        var messages = new List<BmpMessage>();
        while (await BmpMessage.ReadAsync(stream, CancellationToken.None) is { } message)
        {
            messages.Add(message);
        }
        return messages;
    }

    private static async Task<List<(string Prefix, string Path, string? NextHop, bool Withdrawn)>> RoutesAsync(string router)
    {
        return (await MessagesAsync(router))
            .SelectMany(m => m.LocRibRoutes(Arrival))
            .Select(r => (r.Prefix.ToString(), string.Join(" ", r.AsPath), r.NextHop, r.Withdrawn))
            .ToList();
    }

    [Fact]
    public async Task AStreamOpensWithTheRoutersNameAndItsLocRibPeer()
    {
        var messages = await MessagesAsync("transit");
        Assert.Equal(BmpType.Initiation, messages[0].Type);
        Assert.Equal("transit", messages[0].SysName());
        var locRib = messages[1].Peer!;
        Assert.Equal((BmpType.PeerUp, PeerHeader.LocRib, 65000u, "10.10.0.10"), (messages[1].Type, locRib.PeerType, locRib.PeerAs, locRib.BgpId.ToString()));
    }

    [Fact]
    public async Task TheTransitReportsItsChosenPathsWithItsOwnAsInFront()
    {
        Assert.Equal(
            [
                ("203.0.113.0/24", "65000 65001", "10.10.0.11", false),
                ("203.0.113.0/25", "65000 65003", "10.10.0.13", false),
                ("203.0.113.0/25", "", null, true),
            ],
            await RoutesAsync("transit"));
    }

    [Fact]
    public async Task ARouteTheRouterOriginatesGetsItsAsAndNoNextHop()
    {
        var routes = await RoutesAsync("victim");
        Assert.Equal(("203.0.113.0/24", "65001", null, false), routes[0]);
        Assert.Equal(("203.0.113.0/25", "65001 65000 65003", "10.10.0.13", false), routes[1]);
    }

    [Fact]
    public async Task TheAttackerReportsTheHijackAsItsOwnRoutesAndThenTheReset()
    {
        Assert.Equal(
            [
                ("203.0.113.0/24", "65003 65000 65001", "10.10.0.11", false),
                ("203.0.113.0/24", "65003", null, false),
                ("203.0.113.0/25", "65003", null, false),
                ("203.0.113.0/24", "65003 65000 65001", "10.10.0.11", false),
                ("203.0.113.0/25", "", null, true),
            ],
            await RoutesAsync("attacker"));
    }

    [Fact]
    public async Task EveryRouteIsSeenFromTheReportingRouterAtItsArrival()
    {
        var routes = (await MessagesAsync("victim")).SelectMany(m => m.LocRibRoutes(Arrival)).ToList();
        Assert.All(routes, r => Assert.Equal(("10.10.0.11", Arrival), (r.Peer, r.SeenAt)));
    }

    [Fact]
    public async Task OnlyLocRibRouteMonitoringCarriesRoutes()
    {
        var messages = await MessagesAsync("transit");
        Assert.All(messages.Where(m => m.Type != BmpType.RouteMonitoring || m.Peer!.PeerType != PeerHeader.LocRib),
            m => Assert.Empty(m.LocRibRoutes(Arrival)));
    }

    [Fact]
    public async Task AnotherVersionIsRefused()
    {
        using var stream = new MemoryStream([2, 0, 0, 0, 6, 4]);
        await Assert.ThrowsAsync<InvalidDataException>(() => BmpMessage.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ALengthShorterThanTheHeaderIsRefused()
    {
        using var stream = new MemoryStream([3, 0, 0, 0, 5, 4]);
        await Assert.ThrowsAsync<InvalidDataException>(() => BmpMessage.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task AMessageCutShortEndsTheStream()
    {
        using var stream = new MemoryStream([3, 0, 0, 0, 60, 0, 3]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => BmpMessage.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ARouteMonitoringMessageWithoutItsPeerHeaderIsRefused()
    {
        using var stream = new MemoryStream([3, 0, 0, 0, 10, 0, 3, 0, 0, 0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => BmpMessage.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ARouterWithAFourOctetPrivateAsReportsTheRoutesItOriginates()
    {
        const uint routerAs = 4200000001;
        byte[] peer = [PeerHeader.LocRib, 0, .. new byte[8], .. new byte[12], 10, 10, 0, 20, .. BigEndian(routerAs, 4), 10, 10, 0, 20, .. new byte[8]];
        var update = UpdateMessage(attributes: [.. Attribute(2, []), .. Attribute(3, [0, 0, 0, 0])], nlri: Prefix("203.0.113.0/24"));
        byte[] message = [3, .. BigEndian((uint)(6 + peer.Length + update.Length), 4), (byte)BmpType.RouteMonitoring, .. peer, .. update];
        using var stream = new MemoryStream(message);
        var route = Assert.Single((await BmpMessage.ReadAsync(stream, CancellationToken.None))!.LocRibRoutes(Arrival));
        Assert.Equal([routerAs], route.AsPath);
        Assert.Equal(routerAs, route.OriginAs);
    }

    [Fact]
    public async Task AnIpv6PeerAddressIsReadWhenItsFlagIsSet()
    {
        byte[] peer = [0, 0x80, .. new byte[8], 0x20, 0x01, 0x0d, 0xb8, .. new byte[11], 1, .. BigEndian(65010, 4), 10, 0, 0, 1, .. new byte[8]];
        byte[] message = [3, .. BigEndian((uint)(6 + peer.Length), 4), (byte)BmpType.PeerDown, .. peer];
        using var stream = new MemoryStream(message);
        var read = await BmpMessage.ReadAsync(stream, CancellationToken.None);
        Assert.Equal("2001:db8::1", read!.Peer!.Address.ToString());
    }
}
