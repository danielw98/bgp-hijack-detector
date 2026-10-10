using BGPWatch.Core.Domain;

namespace BGPWatch.Tests;

/// <summary>Kernel forwarding verdicts, from iproute2 6.15 output captured in the lab on 2026-10-09.</summary>
public class KernelWalkTests
{
    private const string Via = "203.0.113.1 via 10.10.0.11 dev eth0 src 10.10.0.10 uid 0 \n    cache \n";
    private const string Local = "local 203.0.113.1 dev lo src 203.0.113.1 uid 0 \n    cache <local> \n";
    private const string Direct = "10.10.0.11 dev eth0 src 10.10.0.10 uid 0 \n    cache \n";
    private const string Blackhole = "RTNETLINK answers: Invalid argument\n";
    private const string Unreachable = "RTNETLINK answers: Network unreachable\n";

    private static readonly Dictionary<string, string> Routers = new()
    {
        ["10.10.0.11"] = "victim",
        ["10.10.0.10"] = "transit",
        ["10.10.0.13"] = "attacker",
    };

    [Fact]
    public void ReadsTheNextHopOfAForwardedPacket()
    {
        Assert.Equal(new Verdict("via", "10.10.0.11", "eth0"), KernelWalk.Read(Via, ""));
    }

    [Fact]
    public void TellsLocalDeliveryFromADirectLink()
    {
        Assert.Equal(new Verdict("local", null, "lo"), KernelWalk.Read(Local, ""));
        Assert.Equal(new Verdict("direct", null, "eth0"), KernelWalk.Read(Direct, ""));
    }

    [Fact]
    public void ReadsBlackholeAndUnreachableFromTheError()
    {
        Assert.Equal("blackhole", KernelWalk.Read("", Blackhole).Kind);
        Assert.Equal("unreachable", KernelWalk.Read("", Unreachable).Kind);
        Assert.Equal("unknown", KernelWalk.Read("", "").Kind);
    }

    private static (List<(string, string)> Asked, Func<string, string, Task<Verdict>> Ask) Answers(Dictionary<string, Verdict> table)
    {
        var asked = new List<(string, string)>();
        return (asked, (router, destination) =>
        {
            asked.Add((router, destination));
            return Task.FromResult(table[router]);
        });
    }

    [Fact]
    public async Task AtBaselineThePacketIsDeliveredAtTheVictim()
    {
        var (asked, ask) = Answers(new() { ["transit"] = KernelWalk.Read(Via, ""), ["victim"] = KernelWalk.Read(Local, "") });
        var walk = await KernelWalk.FollowAsync("transit", "203.0.113.1", Routers, ask);
        Assert.Equal("delivered", walk.Outcome);
        Assert.Equal([("transit", "via"), ("victim", "local")], walk.Hops.Select(h => (h.Router, h.Kind)));
        Assert.Equal([("transit", "203.0.113.1"), ("victim", "203.0.113.1")], asked);
    }

    [Fact]
    public async Task DuringTheHijackThePacketIsDroppedAtTheAttacker()
    {
        var (_, ask) = Answers(new()
        {
            ["transit"] = KernelWalk.Read(Via.Replace("10.10.0.11", "10.10.0.13"), ""),
            ["attacker"] = KernelWalk.Read("", Blackhole),
        });
        var walk = await KernelWalk.FollowAsync("transit", "203.0.113.1", Routers, ask);
        Assert.Equal("dropped", walk.Outcome);
        Assert.Equal(["transit", "attacker"], walk.Hops.Select(h => h.Router));
    }

    [Fact]
    public async Task StopsWhenTheNextHopIsNoLabRouter()
    {
        var (_, ask) = Answers(new() { ["transit"] = KernelWalk.Read(Via.Replace("10.10.0.11", "10.10.0.99"), "") });
        Assert.Equal("lost", (await KernelWalk.FollowAsync("transit", "203.0.113.1", Routers, ask)).Outcome);
    }

    [Fact]
    public async Task GivesUpOnALoop()
    {
        var (_, ask) = Answers(new()
        {
            ["transit"] = KernelWalk.Read(Via, ""),
            ["victim"] = KernelWalk.Read(Via.Replace("10.10.0.11", "10.10.0.10"), ""),
        });
        var walk = await KernelWalk.FollowAsync("transit", "203.0.113.1", Routers, ask);
        Assert.Equal("loop", walk.Outcome);
        Assert.Equal(KernelWalk.MaxHops, walk.Hops.Count);
    }
}
