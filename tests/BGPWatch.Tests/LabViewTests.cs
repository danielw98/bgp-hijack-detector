using BGPWatch.Web.Lab;

namespace BGPWatch.Tests;

/// <summary>
/// Lab-view tests against vtysh output captured from the FRR 10.4.5 lab during the hijack
/// (2026-10-09); the neighbors document is the transit's with route-map ROV applied (2026-10-16), trimmed.
/// </summary>
public class LabViewTests
{
    private const string Summary = """
        {
          "routerId":"10.10.0.10",
          "as":65000,
          "peers":{
            "10.10.0.11":{"hostname":"victim","remoteAs":65001,"state":"Established","peerState":"OK"},
            "10.10.0.13":{"hostname":"attacker","remoteAs":65003,"state":"Active","peerState":"OK"}
          },
          "totalPeers":2
        }
        """;

    private const string TransitTable = """
        {
         "vrfId": 0,
         "routerId": "10.10.0.10",
         "localAS": 65000,
         "routes": { "203.0.113.0/24": [{"valid":true,"pathFrom":"external","peerId":"10.10.0.13","path":"65003","origin":"IGP","nexthops":[{"ip":"10.10.0.13","hostname":"attacker","afi":"ipv4","used":true}]},{"valid":true,"bestpath":true,"selectionReason":"Older Path","pathFrom":"external","peerId":"10.10.0.11","path":"65001","origin":"IGP","nexthops":[{"ip":"10.10.0.11","hostname":"victim","afi":"ipv4","used":true}]}]
        ,"203.0.113.0/25": [{"valid":true,"bestpath":true,"selectionReason":"First path received","pathFrom":"external","peerId":"10.10.0.13","path":"65003","origin":"IGP","nexthops":[{"ip":"10.10.0.13","hostname":"attacker","afi":"ipv4","used":true}]}]
         } , "totalRoutes": 2, "totalPaths": 3 }
        """;

    private const string VictimTable = """
        {
         "localAS": 65001,
         "routes": { "203.0.113.0/24": [{"valid":true,"bestpath":true,"selectionReason":"First path received","weight":32768,"peerId":"(unspec)","path":"","origin":"IGP","nexthops":[{"ip":"0.0.0.0","hostname":"victim","afi":"ipv4","used":true}]}]
        ,"203.0.113.0/25": [{"valid":true,"bestpath":true,"selectionReason":"First path received","peerId":"10.10.0.10","path":"65000 65003","origin":"IGP","nexthops":[{"ip":"10.10.0.10","hostname":"transit","afi":"ipv4","used":true}]}]
         } , "totalRoutes": 2, "totalPaths": 2 }
        """;

    private const string TransitNeighbors = """
        {
          "10.10.0.11":{"remoteAs":65001,"bgpState":"Established","addressFamilyInfo":{"ipv4Unicast":{"acceptedPrefixCounter":1,"sentPrefixCounter":2}}},
          "10.10.0.13":{"remoteAs":65003,"bgpState":"Established","addressFamilyInfo":{"ipv4Unicast":{"routeMapForIncomingAdvertisements":"ROV","acceptedPrefixCounter":0}}}
        }
        """;

    private const string VictimNeighbors = """
        {"10.10.0.10":{"remoteAs":65000,"bgpState":"Established","addressFamilyInfo":{"ipv4Unicast":{"acceptedPrefixCounter":1}}}}
        """;

    private static string Show(string table, string neighbors = VictimNeighbors)
    {
        return Summary + "\n" + table + "\n" + neighbors;
    }

    private static List<LabPath> PathsOf(string table)
    {
        return LabView.Paths(LabView.SplitDocuments(Show(table)).Table);
    }

    [Fact]
    public void SplitDocumentsReturnsSummaryTableAndNeighbors()
    {
        var (summary, table, neighbors) = LabView.SplitDocuments("% Can't open configuration file\n" + Show(TransitTable, TransitNeighbors));
        Assert.Equal(65000, summary.GetProperty("as").GetInt32());
        Assert.Equal(65000, table.GetProperty("localAS").GetInt32());
        Assert.True(neighbors.TryGetProperty("10.10.0.13", out _));
    }

    [Fact]
    public void SplitDocumentsRejectsOutputWithTwoDocuments()
    {
        Assert.Throws<FormatException>(() => LabView.SplitDocuments(Summary + "\n" + TransitTable));
    }

    [Fact]
    public void RovIsAppliedWhenANeighborsRoutesPassRouteMapRov()
    {
        Assert.True(LabView.RovApplied(LabView.SplitDocuments(Show(TransitTable, TransitNeighbors)).Neighbors));
    }

    [Fact]
    public void RovIsNotAppliedWithoutAnInboundRouteMap()
    {
        Assert.False(LabView.RovApplied(LabView.SplitDocuments(Show(VictimTable)).Neighbors));
    }

    [Fact]
    public void RovIsNotAppliedWhenTheRouterHasNoNeighbors()
    {
        Assert.False(LabView.RovApplied(LabView.SplitDocuments("{} {} {}").Neighbors));
    }

    [Fact]
    public void SplitDocumentsRejectsOutputWithoutJson()
    {
        Assert.Throws<FormatException>(() => LabView.SplitDocuments("bgpd is not running\n"));
    }

    [Fact]
    public void SessionsReportEachNeighborAndWhetherItIsEstablished()
    {
        var (summary, _, _) = LabView.SplitDocuments(Show(TransitTable));
        Assert.Equal([new Session("10.10.0.11", 65001, true), new Session("10.10.0.13", 65003, false)], LabView.Sessions(summary));
    }

    [Fact]
    public void PathsPutTheChosenPathOfAPrefixFirst()
    {
        var exact = PathsOf(TransitTable).Where(p => p.Prefix == "203.0.113.0/24").ToList();
        Assert.Equal([(65001, true), (65003, false)], exact.Select(p => (p.OriginAs, p.Best)));
        Assert.Equal("Older Path", exact[0].Reason);
    }

    [Fact]
    public void PathsKeepTheLosingPathWithoutAReason()
    {
        var spare = PathsOf(TransitTable)[1];
        Assert.Equal(("203.0.113.0/24", 65003, "10.10.0.13", false, ""), (spare.Prefix, spare.OriginAs, spare.NextHop, spare.Best, spare.Reason));
        Assert.Equal([65003], spare.AsPath);
    }

    [Fact]
    public void ALocalPathHasAnEmptyAsPathTheRouterAsOriginAndNoNextHop()
    {
        var local = PathsOf(VictimTable)[0];
        Assert.Empty(local.AsPath);
        Assert.Equal(65001, local.OriginAs);
        Assert.Null(local.NextHop);
    }

    [Fact]
    public void ALearnedPathTakesItsOriginFromTheLastAsAndItsNextHopFromTheNeighbor()
    {
        var learned = PathsOf(VictimTable)[1];
        Assert.Equal([65000, 65003], learned.AsPath);
        Assert.Equal((65003, "10.10.0.10"), (learned.OriginAs, learned.NextHop));
    }

    [Fact]
    public void ATableWithoutRoutesHasNoPaths()
    {
        Assert.Empty(LabView.Paths(LabView.SplitDocuments("{} {} {}").Table));
    }

    [Fact]
    public async Task SnapshotReadsEveryRouterWithTheShowCommand()
    {
        var asked = new List<(string, IReadOnlyList<string>)>();
        var states = await LabView.SnapshotAsync((container, command) =>
        {
            asked.Add((container, command));
            return Task.FromResult(Show(VictimTable));
        }, []);
        Assert.Equal(["victim", "transit", "attacker"], states.Select(s => s.Role));
        Assert.All(states, s => Assert.True(s.Reachable));
        Assert.Equal(LabView.Routers.Select(r => r.Container), asked.Select(a => a.Item1));
        Assert.All(asked, a => Assert.Equal(LabView.ShowCommand, a.Item2));
    }

    [Fact]
    public async Task SnapshotNamesTheAddressesARouterAnswersFor()
    {
        var states = await LabView.SnapshotAsync((_, _) => Task.FromResult(Show(VictimTable)), []);
        Assert.Equal(["203.0.113.1", "203.0.113.129"], states[0].Hosts);
        Assert.Empty(states[1].Hosts);
    }

    [Fact]
    public async Task SnapshotTellsWhichRoutersReportOverBmp()
    {
        var states = await LabView.SnapshotAsync((_, _) => Task.FromResult(Show(VictimTable)), ["10.10.0.10"]);
        Assert.Equal([false, true, false], states.Select(s => s.Reporting));
    }

    [Fact]
    public async Task SnapshotTellsWhichRoutersApplyOriginValidation()
    {
        var states = await LabView.SnapshotAsync((container, _) =>
            Task.FromResult(container == "lab-transit" ? Show(TransitTable, TransitNeighbors) : Show(VictimTable)), []);
        Assert.Equal([false, true, false], states.Select(s => s.Rov));
    }

    [Fact]
    public async Task SnapshotDrawsAFailingRouterAsUnreachable()
    {
        var states = await LabView.SnapshotAsync((_, _) => throw new DockerException("container is not running"), []);
        var victim = states[0];
        Assert.Equal(("victim", "10.10.0.11", 65001, false, false), (victim.Role, victim.Address, victim.Asn, victim.Reachable, victim.Rov));
        Assert.Equal(["203.0.113.1", "203.0.113.129"], victim.Hosts);
        Assert.Empty(victim.Sessions);
        Assert.Empty(victim.Paths);
    }
}
