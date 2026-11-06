using System.Net;
using BGPWatch.Core.Domain;

namespace BGPWatch.Tests;

/// <summary>Alerts grouped into incidents as they arrive, and the table of routes that keeps an incident open.</summary>
public class IncidentTrackerTests
{
    private static readonly IPNetwork Watched = IPNetwork.Parse("203.0.113.0/24");
    private static readonly DateTime Start = new(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);
    private const string Peer = "rrc21 192.0.2.1";
    private const string OtherPeer = "rrc00 198.51.100.1";

    private static Alert Raised(string prefix, long origin, DateTime at, string peer = Peer, IPNetwork? covering = null)
    {
        var type = covering is null ? AlertType.OriginHijack : AlertType.SubprefixHijack;
        return new Alert(type, IPNetwork.Parse(prefix), origin, peer, [origin], $"{prefix} by AS{origin}", new HashSet<long> { 65001 }, covering, at);
    }

    private static Announcement Route(string prefix, long origin, string peer = Peer)
    {
        return Announcement.Of(IPNetwork.Parse(prefix), [64500, origin], peer, "192.0.2.1", Start);
    }

    private static IncidentTracker Tracker()
    {
        return new IncidentTracker([Watched], Gap);
    }

    private static bool Gone(string prefix, long? origin)
    {
        return false;
    }

    [Fact]
    public void TheFirstAlertOpensAnIncident()
    {
        var opened = Assert.Single(Tracker().Offer([Raised("203.0.113.0/24", 65003, Start)]));
        Assert.Equal(IncidentChange.Opened, opened.Change);
        var incident = opened.Incident;
        Assert.Equal(("203.0.113.0/24", 65003L, 1, Start, Start), (incident.Prefix, incident.Origin, incident.Alerts, incident.FirstSeen, incident.LastSeen));
        Assert.Equal([65001], incident.ExpectedOrigins);
        Assert.Equal(["origin_hijack"], incident.AlertTypes);
        Assert.Equal([Peer], incident.Peers);
        Assert.Equal("203.0.113.0/24 by AS65003", incident.Detail);
    }

    [Fact]
    public void ALaterAlertExtendsTheOpenIncident()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        var extended = Assert.Single(tracker.Offer([Raised("203.0.113.0/24", 65003, Start.AddMinutes(5), OtherPeer)]));
        Assert.Equal(IncidentChange.Extended, extended.Change);
        Assert.Equal((2, Start, Start.AddMinutes(5)), (extended.Incident.Alerts, extended.Incident.FirstSeen, extended.Incident.LastSeen));
        Assert.Equal([OtherPeer, Peer], extended.Incident.Peers);
        Assert.Single(tracker.Open);
    }

    [Fact]
    public void ASubprefixAlertJoinsTheIncidentOfItsCoveringPrefix()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        var extended = Assert.Single(tracker.Offer([Raised("203.0.113.0/25", 65003, Start.AddMinutes(1), covering: Watched)]));
        Assert.Equal((IncidentChange.Extended, "203.0.113.0/24"), (extended.Change, extended.Incident.Prefix));
        Assert.Equal(["203.0.113.0/24", "203.0.113.0/25"], extended.Incident.Prefixes);
        Assert.Equal(["origin_hijack", "subprefix_hijack"], extended.Incident.AlertTypes);
    }

    [Fact]
    public void AnotherOriginIsAnotherIncident()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        var opened = Assert.Single(tracker.Offer([Raised("203.0.113.0/24", 65004, Start)]));
        Assert.Equal((IncidentChange.Opened, 65004L), (opened.Change, opened.Incident.Origin));
        Assert.Equal(2, tracker.Open.Count);
    }

    [Fact]
    public void OneBatchReportsAnIncidentOnce()
    {
        var events = Tracker().Offer([Raised("203.0.113.0/24", 65003, Start), Raised("203.0.113.0/24", 65003, Start.AddSeconds(1), OtherPeer)]);
        var opened = Assert.Single(events);
        Assert.Equal((IncidentChange.Opened, 2), (opened.Change, opened.Incident.Alerts));
    }

    [Fact]
    public void ASweepBeforeTheGapClosesNothing()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        Assert.Empty(tracker.Sweep(Start.AddMinutes(9), Gone));
        Assert.Single(tracker.Open);
    }

    [Fact]
    public void ASweepAfterTheGapClosesAnIncidentWhoseRoutesAreGone()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        var closed = Assert.Single(tracker.Sweep(Start.AddMinutes(11), Gone));
        Assert.Equal((IncidentChange.Closed, "203.0.113.0/24", 65003L), (closed.Change, closed.Incident.Prefix, closed.Incident.Origin));
        Assert.Empty(tracker.Open);
    }

    [Fact]
    public void ARouteStillHeldKeepsTheIncidentOpenPastTheGap()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        Assert.Empty(tracker.Sweep(Start.AddDays(1), (prefix, origin) => prefix == "203.0.113.0/24" && origin == 65003));
        Assert.Single(tracker.Open);
    }

    [Fact]
    public void AnAlertAfterTheCloseOpensANewIncident()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        tracker.Sweep(Start.AddMinutes(11), Gone);
        var opened = Assert.Single(tracker.Offer([Raised("203.0.113.0/24", 65003, Start.AddMinutes(12))]));
        Assert.Equal((IncidentChange.Opened, 1, Start.AddMinutes(12)), (opened.Change, opened.Incident.Alerts, opened.Incident.FirstSeen));
    }

    [Fact]
    public void OpenListsTheNewestFirst()
    {
        var tracker = Tracker();
        tracker.Offer([Raised("203.0.113.0/24", 65003, Start)]);
        tracker.Offer([Raised("203.0.113.0/24", 65004, Start.AddMinutes(1))]);
        Assert.Equal([65004L, 65003L], tracker.Open.Select(i => i.Origin));
    }

    [Fact]
    public void TheTableKeepsTheLastRoutePerPeerAndPrefix()
    {
        var table = new RouteTable([Watched]);
        table.Apply([Route("203.0.113.0/24", 65001), Route("203.0.113.0/24", 65003), Route("203.0.113.0/24", 65001, OtherPeer)]);
        Assert.Equal(2, table.Routes.Count);
        Assert.Equal(65003, table.Routes.Single(r => r.Peer == Peer).OriginAs);
    }

    [Fact]
    public void AWithdrawalRemovesThePeersRoute()
    {
        var table = new RouteTable([Watched]);
        table.Apply([Route("203.0.113.0/24", 65003)]);
        table.Apply([Announcement.Withdrawal(Watched, Peer, Start.AddMinutes(1))]);
        Assert.Empty(table.Routes);
        Assert.Empty(table.Within(Watched));
    }

    [Fact]
    public void RoutesOutsideTheMonitoredPrefixesAreNotKept()
    {
        var table = new RouteTable([Watched]);
        table.Apply([Route("198.51.100.0/24", 65003), Route("203.0.113.128/25", 65003)]);
        Assert.Equal("203.0.113.128/25", Assert.Single(table.Routes).Prefix.ToString());
    }

    [Fact]
    public void WithinFindsTheRoutesInsideThePrefix()
    {
        var table = new RouteTable([Watched]);
        table.Apply([Route("203.0.113.128/25", 65003)]);
        Assert.Equal(65003, Assert.Single(table.Within(Watched)).OriginAs);
        Assert.Empty(table.Within(IPNetwork.Parse("203.0.113.0/25")));
    }

    [Fact]
    public void RekeyDropsTheRoutesOutsideTheNewPrefixes()
    {
        var table = new RouteTable([Watched]);
        table.Apply([Route("203.0.113.0/24", 65003)]);
        table.Rekey([IPNetwork.Parse("198.51.100.0/24")]);
        Assert.Empty(table.Routes);
    }
}
