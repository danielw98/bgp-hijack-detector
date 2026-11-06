using System.Text.Json.Nodes;
using BGPWatch.Web.Config;
using BGPWatch.Web.Watch;

namespace BGPWatch.Tests;

/// <summary>The RIS Live frames, decoded from captures shaped like the live stream, and the subscriptions sent for a watch file.</summary>
public class RisLiveTests
{
    private static readonly string[] Lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ris-live.jsonl"));

    [Fact]
    public void AnUpdateYieldsOneAnnouncementPerPrefixAndOnePerWithdrawal()
    {
        var frame = RisFrame.Parse(Lines[0]);
        Assert.Equal(RisFrameKind.Update, frame.Kind);
        Assert.Equal(3, frame.Routes.Count);
        var first = frame.Routes[0];
        Assert.Equal(("193.0.0.0/21", 3333L, "rrc12 80.81.195.178", "80.81.195.178", false), (first.Prefix.ToString(), first.OriginAs, first.Peer, first.NextHop, first.Withdrawn));
        Assert.Equal([57976, 3356, 3333], first.AsPath);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1762456801.37), first.SeenAt);
        Assert.Equal("193.0.10.0/23", frame.Routes[1].Prefix.ToString());
        var withdrawn = frame.Routes[2];
        Assert.Equal(("2001:67c:2e8::/48", true, "rrc12 80.81.195.178"), (withdrawn.Prefix.ToString(), withdrawn.Withdrawn, withdrawn.Peer));
    }

    [Fact]
    public void AnAsSetAtTheEndIsFlattenedAndItsLastMemberIsTheOrigin()
    {
        var route = Assert.Single(RisFrame.Parse(Lines[1]).Routes);
        Assert.Equal([2914, 1299, 64496, 64497], route.AsPath);
        Assert.Equal((64497L, "2001:db8::/32", "rrc01 2001:7f8:1::a500:2914:1"), (route.OriginAs, route.Prefix.ToString(), route.Peer));
    }

    [Fact]
    public void AWithdrawalOnlyMessageYieldsWithdrawals()
    {
        var route = Assert.Single(RisFrame.Parse(Lines[2]).Routes);
        Assert.Equal(("198.51.100.0/24", true, "rrc00 1.2.3.4"), (route.Prefix.ToString(), route.Withdrawn, route.Peer));
        Assert.Null(route.OriginAs);
    }

    [Theory]
    [InlineData(3, RisFrameKind.Other)]
    [InlineData(4, RisFrameKind.SubscribeOk)]
    [InlineData(5, RisFrameKind.Pong)]
    [InlineData(6, RisFrameKind.Error)]
    public void OtherFramesCarryNoRoutes(int line, RisFrameKind kind)
    {
        var frame = RisFrame.Parse(Lines[line]);
        Assert.Equal(kind, frame.Kind);
        Assert.Empty(frame.Routes);
    }

    [Fact]
    public void AnErrorCarriesTheServersMessage()
    {
        Assert.Equal("Invalid prefix", RisFrame.Parse(Lines[6]).Message);
    }

    [Fact]
    public void AFrameThatIsNotAnObjectIsRefused()
    {
        Assert.Throws<FormatException>(() => RisFrame.Parse("[1, 2]"));
    }

    [Fact]
    public void TheSubscriptionAsksForTheUpdatesOfThePrefixAndItsMoreSpecificsAcknowledged()
    {
        var message = JsonNode.Parse(new RisSubscription("193.0.0.0/21").ToJson())!;
        Assert.Equal("ris_subscribe", message["type"]!.GetValue<string>());
        var data = message["data"]!;
        Assert.Equal(("193.0.0.0/21", true, "UPDATE"), (data["prefix"]!.GetValue<string>(), data["moreSpecific"]!.GetValue<bool>(), data["type"]!.GetValue<string>()));
        Assert.True(data["socketOptions"]!["acknowledge"]!.GetValue<bool>());
        Assert.Null(data["host"]);
        Assert.Equal("rrc21", JsonNode.Parse(new RisSubscription("193.0.0.0/21", "rrc21").ToJson())!["data"]!["host"]!.GetValue<string>());
    }

    [Fact]
    public void TheSubscriptionsCoverEveryPrefixAtEveryCollector()
    {
        var settings = new WatchSettings
        {
            Monitored = [new MonitoredEntry("193.0.0.0/21", [3333]), new MonitoredEntry("2001:db8::/32", [64496])],
            Ris = new RisEntry { Hosts = ["rrc00", "rrc21"] },
        };
        Assert.Equal(
            [new("193.0.0.0/21", "rrc00"), new("193.0.0.0/21", "rrc21"), new("2001:db8::/32", "rrc00"), new("2001:db8::/32", "rrc21")],
            WatchService.Subscriptions(settings));
        Assert.Equal([new RisSubscription("193.0.0.0/21"), new RisSubscription("2001:db8::/32")], WatchService.Subscriptions(settings with { Ris = new RisEntry() }));
    }
}
