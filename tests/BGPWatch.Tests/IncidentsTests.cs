using BGPWatch.Core.Domain;

namespace BGPWatch.Tests;

public class IncidentsTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 19, 2, 48, DateTimeKind.Utc);
    private static readonly string[] Monitored = ["203.0.113.0/24", "208.65.152.0/22"];

    private static StoredAlert Alert(long id, DateTime at, string type, string prefix, string peer, string? covering = null)
    {
        long[] expected = type != "rpki_invalid" ? [65001] : [];
        return new StoredAlert(id, at, type, prefix, 65003, expected, covering, peer, [], "", "lab");
    }

    private static readonly StoredAlert[] LabHijack =
    [
        Alert(1, T0, "origin_hijack", "203.0.113.0/24", "10.10.0.13"),
        Alert(2, T0, "rpki_invalid", "203.0.113.0/24", "10.10.0.13"),
        Alert(3, T0, "subprefix_hijack", "203.0.113.0/25", "10.10.0.13", "203.0.113.0/24"),
        Alert(4, T0, "rpki_invalid", "203.0.113.0/25", "10.10.0.13"),
        Alert(5, T0.AddMilliseconds(90), "subprefix_hijack", "203.0.113.0/25", "10.10.0.10", "203.0.113.0/24"),
        Alert(6, T0.AddMilliseconds(90), "rpki_invalid", "203.0.113.0/25", "10.10.0.10"),
        Alert(7, T0.AddMilliseconds(110), "subprefix_hijack", "203.0.113.0/25", "10.10.0.11", "203.0.113.0/24"),
        Alert(8, T0.AddMilliseconds(110), "rpki_invalid", "203.0.113.0/25", "10.10.0.11"),
    ];

    private static IEnumerable<StoredAlert> Shifted(TimeSpan by)
    {
        return LabHijack.Select(a => a with { Id = a.Id + 10, RaisedAt = a.RaisedAt + by });
    }

    [Fact]
    public void TheEightAlertsOfOneLabHijackFormOneIncident()
    {
        var incident = Assert.Single(Incidents.Group(LabHijack, Monitored));
        Assert.Equal("203.0.113.0/24", incident.Prefix);
        Assert.Equal(65003, incident.Origin);
        Assert.Equal([65001], incident.ExpectedOrigins);
        Assert.Equal(["10.10.0.10", "10.10.0.11", "10.10.0.13"], incident.Peers);
        Assert.Equal(["origin_hijack", "rpki_invalid", "subprefix_hijack"], incident.AlertTypes);
        Assert.Equal(["203.0.113.0/24", "203.0.113.0/25"], incident.Prefixes);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), incident.AlertIds);
        Assert.Equal((T0, T0.AddMilliseconds(110)), (incident.FirstSeen, incident.LastSeen));
    }

    [Fact]
    public void APauseLongerThanTheGapStartsANewIncidentNewestFirst()
    {
        var grouped = Incidents.Group([.. LabHijack, .. Shifted(TimeSpan.FromMinutes(30))], Monitored);
        Assert.Equal([T0.AddMinutes(30), T0], grouped.Select(i => i.FirstSeen));
        Assert.Equal([8, 8], grouped.Select(i => i.AlertIds.Count));
    }

    [Fact]
    public void AlertsWithinTheGapExtendTheOpenIncident()
    {
        var incident = Assert.Single(Incidents.Group([.. LabHijack, .. Shifted(TimeSpan.FromMinutes(9))], Monitored));
        Assert.Equal(16, incident.AlertIds.Count);
    }

    [Fact]
    public void SourcesAndOriginsAreKeptApart()
    {
        var replay = Alert(20, T0, "subprefix_hijack", "208.65.153.0/24", "193.0.0.56", "208.65.152.0/22")
            with { Source = "youtube-2008", ObservedOrigin = 17557 };
        var otherOrigin = Alert(30, T0, "origin_hijack", "203.0.113.0/24", "10.10.0.13") with { ObservedOrigin = 65002 };
        var grouped = Incidents.Group([.. LabHijack, replay, otherOrigin], Monitored);
        Assert.Equal(
            [("lab", 65002), ("lab", 65003), ("youtube-2008", 17557)],
            grouped.Select(i => (i.Source, i.Origin ?? 0)).Order());
    }

    [Fact]
    public void AnAlertOutsideEveryMonitoredPrefixKeepsItsOwnPrefix()
    {
        var incident = Assert.Single(Incidents.Group([Alert(40, T0, "rpki_invalid", "198.51.100.0/24", "10.10.0.13")], Monitored));
        Assert.Equal("198.51.100.0/24", incident.Prefix);
    }
}
