using System.Net;
using BGPWatch.Core.Domain;

namespace BGPWatch.Tests;

public class DetectTests
{
    private static readonly MonitoredPrefix[] Monitored = [new(IPNetwork.Parse("203.0.113.0/24"), new HashSet<long> { 65001 }, "lab victim")];
    private static readonly Roa[] Vrps = [new(IPNetwork.Parse("203.0.113.0/24"), 24, 65001)];

    /// <summary>A customer /24 originated by its own AS inside its provider's /22.</summary>
    private static readonly MonitoredPrefix[] Nested =
    [
        new(IPNetwork.Parse("208.65.152.0/22"), new HashSet<long> { 36561 }, "provider aggregate"),
        new(IPNetwork.Parse("208.65.153.0/24"), new HashSet<long> { 17557 }, "customer"),
    ];
    private static readonly DateTime Now = new(2026, 10, 9, 18, 0, 0, DateTimeKind.Utc);

    private static Announcement Seen(string prefix, int origin, DateTime? at = null)
    {
        return Announcement.Of(IPNetwork.Parse(prefix), [origin], "transit", null, at ?? Now);
    }

    private static HashSet<AlertType> Types(IEnumerable<Alert> alerts)
    {
        return alerts.Select(a => a.Type).ToHashSet();
    }

    [Fact]
    public void AlertCarriesTheTimeTheAnnouncementWasSeen()
    {
        var seenAt = new DateTime(2008, 2, 24, 18, 47, 0, DateTimeKind.Utc);
        var alert = Assert.Single(new HijackDetector(Monitored, []).Inspect(Seen("203.0.113.0/24", 65003, seenAt)));
        Assert.Equal(seenAt, alert.RaisedAt);
    }

    [Fact]
    public void LegitimateAnnouncementIsSilent()
    {
        Assert.Empty(new HijackDetector(Monitored, Vrps).Inspect(Seen("203.0.113.0/24", 65001)));
    }

    [Fact]
    public void ExactPrefixOriginHijack()
    {
        var types = Types(new HijackDetector(Monitored, Vrps).Inspect(Seen("203.0.113.0/24", 65003)));
        Assert.Equal([AlertType.OriginHijack, AlertType.RpkiInvalid], types.Order());
    }

    [Fact]
    public void MoreSpecificSubprefixHijack()
    {
        var types = Types(new HijackDetector(Monitored, Vrps).Inspect(Seen("203.0.113.0/25", 65003)));
        Assert.Equal([AlertType.SubprefixHijack, AlertType.RpkiInvalid], types.Order());
    }

    [Fact]
    public void MoreSpecificFromTheLegitimateOriginIsOnlyRpkiInvalid()
    {
        var types = Types(new HijackDetector(Monitored, Vrps).Inspect(Seen("203.0.113.0/25", 65001)));
        Assert.Equal([AlertType.RpkiInvalid], types);
    }

    [Fact]
    public void WithdrawnUpdateIsIgnored()
    {
        var withdrawal = Announcement.Withdrawal(IPNetwork.Parse("203.0.113.0/24"), "transit", Now);
        Assert.Empty(new HijackDetector(Monitored, Vrps).Inspect(withdrawal));
    }

    [Fact]
    public void UnrelatedPrefixIsSilent()
    {
        Assert.Empty(new HijackDetector(Monitored, Vrps).Inspect(Seen("198.51.100.0/24", 65099)));
    }

    [Fact]
    public void AnAnnouncementWithoutOriginIsAnOriginHijack()
    {
        var local = Announcement.Of(IPNetwork.Parse("203.0.113.0/24"), [], "transit", null, Now);
        var alert = Assert.Single(new HijackDetector(Monitored, []).Inspect(local));
        Assert.Equal("203.0.113.0/24 announced by ASNone, expected AS65001", alert.Detail);
    }

    [Fact]
    public void AMoreSpecificMonitoredPrefixDecidesItsOwnOrigin()
    {
        var detector = new HijackDetector(Nested, []);
        Assert.Empty(detector.Inspect(Seen("208.65.153.0/24", 17557)));
        Assert.Empty(detector.Inspect(Seen("208.65.153.128/25", 17557)));
    }

    [Fact]
    public void AnUnexpectedOriginIsJudgedOnlyByTheMostSpecificMonitoredPrefix()
    {
        var alert = Assert.Single(new HijackDetector(Nested, []).Inspect(Seen("208.65.153.0/24", 64999)));
        Assert.Equal((AlertType.OriginHijack, (IPNetwork?)null), (alert.Type, alert.CoveringPrefix));
    }

    [Fact]
    public void TheOuterMonitoredPrefixStillGovernsTheRestOfItsSpace()
    {
        var alert = Assert.Single(new HijackDetector(Nested, []).Inspect(Seen("208.65.154.0/24", 17557)));
        Assert.Equal((AlertType.SubprefixHijack, IPNetwork.Parse("208.65.152.0/22")), (alert.Type, alert.CoveringPrefix));
    }

    [Fact]
    public void AlertDetailsNameThePrefixesAndOrigins()
    {
        var alerts = new HijackDetector(Monitored, Vrps).Inspect(Seen("203.0.113.0/25", 65003));
        Assert.Equal(
            [
                "more-specific 203.0.113.0/25 of monitored 203.0.113.0/24 announced by AS65003, expected AS65001",
                "203.0.113.0/25 from AS65003 is RPKI-invalid (no VRP authorizes this origin or length)",
            ],
            alerts.Select(a => a.Detail));
    }

    [Theory]
    [InlineData("203.0.113.0/24", 65001, RpkiState.Valid)]
    [InlineData("203.0.113.0/24", 65003, RpkiState.Invalid)]
    [InlineData("203.0.113.0/25", 65001, RpkiState.Invalid)]
    [InlineData("198.51.100.0/24", 65099, RpkiState.NotFound)]
    public void RpkiStates(string prefix, int origin, RpkiState expected)
    {
        Assert.Equal(expected, HijackDetector.RpkiValidity(Seen(prefix, origin), Vrps));
    }

    [Fact]
    public void AnotherFamilyIsNeverCovered()
    {
        Assert.False(IPNetwork.Parse("0.0.0.0/0").Covers(IPNetwork.Parse("2001:db8::/32")));
    }

    [Theory]
    [InlineData("203.0.113.7/24", "203.0.113.0/24")]
    [InlineData("203.0.113.7", "203.0.113.7/32")]
    [InlineData("2001:db8::1/32", "2001:db8::/32")]
    public void LooseParsingClearsTheHostBits(string text, string expected)
    {
        Assert.Equal(expected, Prefixes.ParseLoose(text).ToString());
    }
}
