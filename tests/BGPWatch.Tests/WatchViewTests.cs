using System.Net;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Watch;

namespace BGPWatch.Tests;

/// <summary>What the page is told about each watched prefix: its status, the origins seen with their vantage points, the open incidents.</summary>
public class WatchViewTests
{
    private static readonly DateTime Start = new(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);
    private static readonly MonitoredEntry[] Monitored = [new("203.0.113.0/24", [65001], "lab victim network")];

    private static Announcement Route(string prefix, long origin, string peer, int minute = 0)
    {
        return Announcement.Of(IPNetwork.Parse(prefix), [64500, origin], peer, "192.0.2.1", Start.AddMinutes(minute));
    }

    [Fact]
    public void AnUnexpectedOriginMakesThePrefixAlertAndListsWhoSeesWhat()
    {
        var routes = new[] { Route("203.0.113.0/24", 65001, "rrc00 a"), Route("203.0.113.0/24", 65001, "rrc01 b", 2), Route("203.0.113.0/25", 65003, "rrc21 c", 1) };
        var view = Assert.Single(WatchView.Of(Monitored, routes, []));
        Assert.Equal((WatchView.Alert, Start.AddMinutes(2), "lab victim network"), (view.Status, view.LastUpdate, view.Description));
        Assert.Equal([65001], view.ExpectedOrigins);
        Assert.Equal(2, view.Origins.Count);
        Assert.Equal((65001L, true, 2), (view.Origins[0].Asn, view.Origins[0].Expected, view.Origins[0].Peers));
        Assert.Equal(["203.0.113.0/24"], view.Origins[0].Prefixes);
        Assert.Equal((65003L, false, 1), (view.Origins[1].Asn, view.Origins[1].Expected, view.Origins[1].Peers));
        Assert.Equal(["203.0.113.0/25"], view.Origins[1].Prefixes);
    }

    [Fact]
    public void OnlyExpectedOriginsMakeThePrefixOk()
    {
        var view = Assert.Single(WatchView.Of(Monitored, [Route("203.0.113.0/24", 65001, "rrc00 a")], []));
        Assert.Equal(WatchView.Ok, view.Status);
        Assert.Empty(view.Incidents);
    }

    [Fact]
    public void NoRouteIsUnknown()
    {
        var view = Assert.Single(WatchView.Of(Monitored, [], []));
        Assert.Equal((WatchView.Unknown, (DateTime?)null), (view.Status, view.LastUpdate));
        Assert.Empty(view.Origins);
    }

    [Fact]
    public void ASeedFailureIsNamedOnThePrefix()
    {
        var failures = new Dictionary<string, string> { ["203.0.113.0/24"] = "RIPEstat looking-glass: no answer within 60 s" };
        var view = Assert.Single(WatchView.Of(Monitored, [], [], failures));
        Assert.Equal((WatchView.Unknown, "RIPEstat looking-glass: no answer within 60 s"), (view.Status, view.SeedFailure));
        Assert.Null(Assert.Single(WatchView.Of(Monitored, [], [])).SeedFailure);
    }

    [Fact]
    public void AnOpenIncidentMakesThePrefixAlertEvenBeforeItsRoutesAreKnown()
    {
        var incident = new OpenIncident("203.0.113.0/24", 65003, [65001], Start, Start, 1, ["rrc21 c"], ["203.0.113.0/24"], ["origin_hijack"], "detail");
        var view = Assert.Single(WatchView.Of(Monitored, [], [incident]));
        Assert.Equal(WatchView.Alert, view.Status);
        Assert.Same(incident, Assert.Single(view.Incidents));
    }

    [Fact]
    public void ARouteCountsUnderTheMostSpecificWatchedPrefixOnly()
    {
        MonitoredEntry[] nested = [new("203.0.113.0/24", [65001], "block"), new("203.0.113.128/25", [65002], "customer")];
        var views = WatchView.Of(nested, [Route("203.0.113.128/25", 65002, "rrc00 a"), Route("203.0.113.0/24", 65001, "rrc00 a")], []);
        Assert.Equal([WatchView.Ok, WatchView.Ok], views.Select(v => v.Status));
        Assert.Equal([65001L], views[0].Origins.Select(o => o.Asn));
        Assert.Equal([65002L], views[1].Origins.Select(o => o.Asn));
    }
}
