using BGPWatch.Web.Api;
using BGPWatch.Web.Config;
using BGPWatch.Web.Lab;
using static BGPWatch.Tests.Wire;

namespace BGPWatch.Tests;

/// <summary>The small adapters: the TOML ground truth, the Docker exec stream, and the vtysh scenarios.</summary>
public class AdapterTests
{
    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }

    [Fact]
    public void TheLabConfigHoldsOneMonitoredPrefixAndOneRoa()
    {
        var config = TomlFiles.LoadDetector(Fixture("lab.toml"));
        var monitored = Assert.Single(config.Monitored);
        Assert.Equal(("203.0.113.0/24", "lab victim network"), (monitored.Prefix.ToString(), monitored.Description));
        Assert.Equal([65001], monitored.ExpectedOrigins);
        var roa = Assert.Single(config.Vrps);
        Assert.Equal(("203.0.113.0/24", 24, 65001), (roa.Prefix.ToString(), roa.MaxLength, roa.OriginAs));
    }

    [Fact]
    public void AnIncidentFileHoldsItsTimelineInUtc()
    {
        var incident = TomlFiles.LoadIncident(Fixture("youtube-2008.toml"));
        Assert.Equal("208.65.152.0/22", Assert.Single(incident.Monitored).Prefix);
        Assert.Equal(5, incident.Events.Count);
        Assert.Equal(new DateTime(2008, 2, 24, 18, 47, 0, DateTimeKind.Utc), incident.Events[0].At);
        Assert.Equal(DateTimeKind.Utc, incident.Events[0].At.Kind);
        Assert.StartsWith("AS17557 (Pakistan Telecom) announces", incident.Events[0].Label);
        Assert.StartsWith("AS17557 (Pakistan Telecom) anunță", incident.Events[0].LabelRo);
        Assert.Equal(incident.Events[0].LabelRo, incident.Events[0].LabelIn(BGPWatch.Web.Localization.Language.Ro));
        Assert.Equal(incident.Events[0].Label, incident.Events[0].LabelIn(BGPWatch.Web.Localization.Language.En));
    }

    [Fact]
    public void AnExecStreamIsSplitIntoStdoutAndStderr()
    {
        byte[] stream = [1, 0, 0, 0, .. BigEndian(3, 4), .. "out"u8, 2, 0, 0, 0, .. BigEndian(3, 4), .. "err"u8, 1, 0, 0, 0, .. BigEndian(1, 4), .. "!"u8];
        Assert.Equal(("out!", "err"), DockerEngine.Demultiplex(stream));
    }

    [Fact]
    public void AFrameCutShortKeepsWhatArrived()
    {
        byte[] stream = [1, 0, 0, 0, .. BigEndian(10, 4), .. "abc"u8];
        Assert.Equal(("abc", ""), DockerEngine.Demultiplex(stream));
    }

    [Fact]
    public void TheTriggerAnnouncesEveryPrefixOfTheScenarioInOneCall()
    {
        var batch = Assert.Single(LabEndpoints.VtyshBatches("trigger", ["203.0.113.0/24", "203.0.113.0/25"]));
        Assert.Equal(
            [
                "configure terminal", "ip route 203.0.113.0/24 Null0", "ip route 203.0.113.0/25 Null0",
                "router bgp 65003", "address-family ipv4 unicast", "network 203.0.113.0/24", "network 203.0.113.0/25", "end",
            ],
            batch);
    }

    [Fact]
    public void TheResetWithdrawsEachPrefixAndItsBlackholeInACallOfItsOwn()
    {
        var batches = LabEndpoints.VtyshBatches("reset", ["203.0.113.0/24", "203.0.113.0/25"]);
        Assert.Equal(2, batches.Count);
        Assert.Equal(
            [
                "configure terminal", "router bgp 65003", "address-family ipv4 unicast", "no network 203.0.113.0/25",
                "exit-address-family", "exit", "no ip route 203.0.113.0/25 Null0", "end",
            ],
            batches[1]);
    }
}
