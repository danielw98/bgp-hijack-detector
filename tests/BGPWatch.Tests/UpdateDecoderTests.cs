using System.Net;
using System.Runtime.InteropServices;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Native;
using static BGPWatch.Tests.Wire;

namespace BGPWatch.Tests;

/// <summary>The C parser in detector/mrt, on hand-built records and on the recorded 2008 incident.</summary>
public class UpdateDecoderTests
{
    private static readonly string Incident = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static IReadOnlyList<Announcement> Parse(byte[] data)
    {
        return UpdateDecoder.ReadMrt(data).Routes;
    }

    private static (int Routes, int Malformed) Counts(byte[] data)
    {
        var file = UpdateDecoder.ReadMrt(data);
        return (file.Routes.Count, file.MalformedRecords);
    }

    [Fact]
    public void TheRouteStructHasTheLayoutOfMrtRoute()
    {
        Assert.Equal(8252, Marshal.SizeOf<NativeMrt.Route>());
        Assert.Equal(58, (int)Marshal.OffsetOf<NativeMrt.Route>(nameof(NativeMrt.Route.AsPathLength)));
        Assert.Equal(60, (int)Marshal.OffsetOf<NativeMrt.Route>(nameof(NativeMrt.Route.AsPath)));
    }

    [Fact]
    public void AnnouncedPrefixesSharePathPeerAndNextHop()
    {
        var routes = Parse(Record(Announce));
        Assert.Equal(["203.0.113.0/24", "198.51.100.0/25"], routes.Select(r => r.Prefix.ToString()));
        Assert.All(routes, r =>
        {
            Assert.Equal([64500, 64501], r.AsPath);
            Assert.Equal(64501, r.OriginAs);
            Assert.Equal(Wire.Peer, r.Peer);
            Assert.Equal(Wire.Peer, r.NextHop);
            Assert.False(r.Withdrawn);
        });
    }

    [Fact]
    public void RecordTimestampBecomesSeenAt()
    {
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(Timestamp), Parse(Record(Announce))[0].SeenAt);
    }

    [Fact]
    public void WithdrawnPrefixCarriesNoPath()
    {
        var route = Assert.Single(Parse(Record(UpdateMessage(withdrawn: Prefix("203.0.113.0/24")))));
        Assert.True(route.Withdrawn);
        Assert.Equal("203.0.113.0/24", route.Prefix.ToString());
        Assert.Empty(route.AsPath);
        Assert.Null(route.OriginAs);
        Assert.Null(route.NextHop);
    }

    [Fact]
    public void WithdrawalsComeBeforeAnnouncementsOfTheSameUpdate()
    {
        var message = UpdateMessage(withdrawn: Prefix("198.51.100.0/24"), attributes: Attribute(2, AsPath([64500])), nlri: Prefix("203.0.113.0/24"));
        Assert.Equal([true, false], Parse(Record(message)).Select(r => r.Withdrawn));
    }

    [Fact]
    public void FourOctetSessionReadsFourOctetAsNumbers()
    {
        var message = UpdateMessage(attributes: Attribute(2, AsPath([64500, 65550], width: 4)), nlri: Prefix("203.0.113.0/24"));
        Assert.Equal([64500, 65550], Assert.Single(Parse(Record(message, subtype: MessageAs4))).AsPath);
    }

    [Fact]
    public void As4PathReplacesAsTransOnATwoOctetSession()
    {
        var message = UpdateMessage(
            attributes: [.. Attribute(2, AsPath([64500, AsTrans])), .. Attribute(17, AsPath([65550], width: 4))],
            nlri: Prefix("203.0.113.0/24"));
        Assert.Equal([64500, 65550], Assert.Single(Parse(Record(message))).AsPath);
    }

    [Fact]
    public void AsSetMembersAreFlattenedAfterTheSequence()
    {
        byte[] path = [.. AsPath([64500]), .. AsPath([64501, 64502], segment: AsSet)];
        var message = UpdateMessage(attributes: Attribute(2, path), nlri: Prefix("203.0.113.0/24"));
        Assert.Equal([64500, 64501, 64502], Assert.Single(Parse(Record(message))).AsPath);
    }

    [Fact]
    public void ExtendedLengthAttribute()
    {
        var path = AsPath([64500, 64501]);
        byte[] extended = [0x50, 2, .. BigEndian((uint)path.Length, 2), .. path];
        Assert.Equal([64500, 64501], Assert.Single(Parse(Record(UpdateMessage(attributes: extended, nlri: Prefix("203.0.113.0/24"))))).AsPath);
    }

    [Fact]
    public void Ipv6PrefixesComeFromTheMultiprotocolAttributes()
    {
        var nextHop = IPAddress.Parse("2001:db8::1");
        byte[] reach = [0, 2, 1, 16, .. nextHop.GetAddressBytes(), 0, .. Prefix("2001:db8:1::/48")];
        byte[] unreach = [0, 2, 1, .. Prefix("2001:db8:2::/48")];
        byte[] attributes = [.. Attribute(15, unreach), .. Attribute(2, AsPath([64500])), .. Attribute(14, reach)];
        var routes = Parse(Record(UpdateMessage(attributes: attributes)));
        Assert.Equal([("2001:db8:2::/48", true), ("2001:db8:1::/48", false)], routes.Select(r => (r.Prefix.ToString(), r.Withdrawn)));
        Assert.Equal(nextHop.ToString(), routes[1].NextHop);
        Assert.Equal([64500], routes[1].AsPath);
    }

    [Fact]
    public void BitsPastThePrefixLengthAreCleared()
    {
        var route = Assert.Single(Parse(Record(UpdateMessage(attributes: Attribute(2, AsPath([64500])), nlri: [23, 203, 0, 113]))));
        Assert.Equal("203.0.112.0/23", route.Prefix.ToString());
    }

    [Fact]
    public void MicrosecondRecordType()
    {
        Assert.Equal("203.0.113.0/24", Parse(Record(Announce, type: Bgp4mpEt))[0].Prefix.ToString());
    }

    [Fact]
    public void RecordsWithoutAnUpdateYieldNothing()
    {
        byte[] stateChange = [.. BigEndian(Timestamp, 4), .. BigEndian(Bgp4mp, 2), .. BigEndian(StateChange, 2), .. BigEndian(4, 4), 0, 0, 0, 0];
        var keepalive = Record(Bgp([], Keepalive));
        Assert.Empty(Parse([.. stateChange, .. keepalive]));
    }

    [Fact]
    public void RecordsAreReadInSequence()
    {
        var record = Record(Announce);
        Assert.Equal(6, Parse([.. record, .. record, .. record]).Count);
    }

    [Fact]
    public void EmptyInputYieldsNothing()
    {
        Assert.Empty(Parse([]));
    }

    [Fact]
    public void ATruncatedRecordIsDroppedAndCounted()
    {
        Assert.Equal((0, 1), Counts(Record(Announce)[..^1]));
    }

    [Fact]
    public void APrefixLongerThanItsFamilyDropsItsRecord()
    {
        Assert.Equal((0, 1), Counts(Record(UpdateMessage(nlri: [33, 203, 0, 113, 0, 0]))));
    }

    [Fact]
    public void AnAsPathRunningPastItsAttributeDropsItsRecord()
    {
        byte[] shortPath = [AsSequence, 3, .. BigEndian(64500, 2)];
        var message = UpdateMessage(attributes: Attribute(2, shortPath), nlri: Prefix("203.0.113.0/24"));
        Assert.Equal((0, 1), Counts(Record(message)));
    }

    [Fact]
    public void AMalformedRecordDropsOnlyItself()
    {
        var malformed = Record(UpdateMessage(nlri: [33, 203, 0, 113, 0, 0]));
        Assert.Equal((4, 1), Counts([.. Record(Announce), .. malformed, .. Record(Announce)]));
    }

    [Fact]
    public void ATruncatedLastRecordKeepsTheRecordsBeforeIt()
    {
        Assert.Equal((2, 1), Counts([.. Record(Announce), .. Record(Announce)[..^1]]));
    }

    [Fact]
    public void ABareUpdateReadsFourOctetPathsAndTakesThePeerAndTimeItIsGiven()
    {
        var seenAt = new DateTime(2026, 10, 10, 1, 5, 35, DateTimeKind.Utc);
        var message = UpdateMessage(attributes: Attribute(2, AsPath([65000, 65003], width: 4)), nlri: Prefix("203.0.113.0/25"));
        var route = Assert.Single(UpdateDecoder.ReadUpdate(message, "10.10.0.10", seenAt));
        Assert.Equal(("203.0.113.0/25", 65003, "10.10.0.10", seenAt), (route.Prefix.ToString(), route.OriginAs, route.Peer, route.SeenAt));
        Assert.Equal([65000, 65003], route.AsPath);
    }

    [Fact]
    public void ABareMessageThatIsNoUpdateYieldsNothing()
    {
        Assert.Empty(UpdateDecoder.ReadUpdate(Bgp([], Keepalive), "10.10.0.10", DateTime.UnixEpoch));
    }

    [Fact]
    public void ATruncatedBareUpdateIsAnError()
    {
        Assert.Throws<MalformedUpdateException>(() => UpdateDecoder.ReadUpdate(Announce[..^1], "10.10.0.10", DateTime.UnixEpoch));
    }

    [Fact]
    public void IncidentFileShowsTheHijackAtTheDocumentedMinute()
    {
        var hijacked = IPNetwork.Parse("208.65.153.0/24");
        var first = new MrtArchive().Read(Path.Combine(Incident, "updates.20080224.1845.gz")).Routes.First(r => r.Prefix == hijacked);
        Assert.Equal("2008-02-24 18:47", first.SeenAt.ToString("yyyy-MM-dd HH:mm"));
        Assert.Equal([3491, 17557], first.AsPath.TakeLast(2));
    }

    [Fact]
    public void IncidentReplayRaisesOnlyTheSubprefixHijack()
    {
        var config = TomlFiles.LoadDetector(Path.Combine(Incident, "youtube-2008.toml"));
        var detector = new HijackDetector(config.Monitored, config.Vrps);
        var alerts = new MrtArchive().Read(Path.Combine(Incident, "updates.20080224.1845.gz")).Routes.SelectMany(detector.Inspect).ToList();
        Assert.Equal(27, alerts.Count);
        Assert.All(alerts, a =>
        {
            Assert.Equal(AlertType.SubprefixHijack, a.Type);
            Assert.Equal("208.65.153.0/24", a.Prefix.ToString());
            Assert.Equal(17557, a.ObservedOrigin);
            Assert.Equal("208.65.152.0/22", a.CoveringPrefix.ToString());
        });
    }
}
