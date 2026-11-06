using System.Text.Json.Nodes;
using System.Web;
using BGPWatch.Web.Lookup;

namespace BGPWatch.Tests;

/// <summary>The RIPEstat adapter, against canned answers shaped like the live API gave on 2026-10-09 for 193.0.0.0/21 (RIPE NCC, AS3333).</summary>
public class RipeStatTests
{
    private static readonly Dictionary<string, string> Answers = new()
    {
        ["network-info"] = """{"asns": ["3333"], "prefix": "193.0.0.0/21"}""",
        ["prefix-overview"] = """{"resource": "193.0.0.0/21", "announced": true, "asns": [{"asn": 3333, "holder": "RIPE-NCC-AS Reseaux IP Europeens Network Coordination Centre (RIPE NCC)"}]}""",
        ["rpki-validation"] = """{"resource": "3333", "prefix": "193.0.0.0/21", "status": "valid", "validating_roas": [{"origin": "3333", "prefix": "193.0.0.0/21", "validity": "valid", "max_length": 21}]}""",
        ["whats-my-ip"] = """{"ip": "2a02:2f04::1"}""",
        ["routing-status"] = """{"origins": [{"origin": "3333", "route_objects": ["ripe"]}], "more_specifics": [{"prefix": "193.0.4.0/22", "origin": "3333"}, {"prefix": "193.0.6.0/24", "origin": 64496}], "less_specifics": [], "visibility": {"v4": {"ris_peers_seeing": 322, "total_ris_peers": 322}}}""",
        ["looking-glass"] = """{"rrcs": [{"rrc": "RRC01", "location": "London, United Kingdom", "scope": "global", "peers": [{"asn_origin": "3333", "as_path": "15692 174 1273 3333", "community": "174:21100", "last_updated": "2026-10-08T22:38:44.290000", "prefix": "193.0.0.0/21", "peer": "5.57.80.113", "origin": "IGP", "next_hop": "5.57.80.113", "latest_time": "2026-11-06T18:46:30.750000"}, {"asn_orgin": "3333", "as_path": "", "prefix": "193.0.0.0/21", "peer": "5.57.80.210", "next_hop": "5.57.80.210"}]}, {"rrc": "RRC00", "location": "Amsterdam, Netherlands", "peers": [{"asn_origin": "64497", "as_path": "3333 {64496,64497}", "prefix": "193.0.0.0/21", "peer": "193.0.0.56", "next_hop": "193.0.0.56"}]}]}""",
        ["announced-prefixes"] = """{"prefixes": [{"prefix": "193.0.0.0/21", "timelines": []}, {"prefix": "193.0.10.0/23", "timelines": []}], "resource": "3333"}""",
    };

    private const string InvalidAsn = """{"resource": "64496", "prefix": "193.0.0.0/21", "validating_roas": [{"origin": "3333", "prefix": "193.0.0.0/21", "validity": "invalid_asn", "max_length": 21}], "status": "invalid_asn", "validator": "routinator"}""";

    private sealed class FakeFetch(Dictionary<string, string> answers)
    {
        public List<Uri> Calls { get; } = [];

        public Task<JsonNode?> FetchAsync(string url, CancellationToken cancel)
        {
            var uri = new Uri(url);
            Calls.Add(uri);
            var call = uri.AbsolutePath.Split('/')[2];
            return Task.FromResult<JsonNode?>(new JsonObject { ["status"] = "ok", ["data"] = JsonNode.Parse(answers[call]) });
        }
    }

    private static RipeStat Api(FakeFetch fetch, Func<TimeSpan>? clock = null)
    {
        return new RipeStat(fetch.FetchAsync, clock ?? (() => TimeSpan.Zero));
    }

    [Fact]
    public async Task LookupReportsPrefixOriginHolderAndRpki()
    {
        var result = await Api(new FakeFetch(Answers)).LookupAsync("193.0.6.139", CancellationToken.None);
        Assert.Equal(("193.0.6.139", "193.0.0.0/21", true), (result.Resource, result.Prefix, result.Announced));
        var origin = Assert.Single(result.Origins);
        Assert.Equal(3333, origin.Asn);
        Assert.StartsWith("RIPE-NCC-AS", origin.Holder);
        Assert.Equal("valid", origin.Rpki.Status);
        Assert.Equal([new Roa("193.0.0.0/21", 21, 3333, "valid")], origin.Rpki.Roas);
    }

    [Fact]
    public async Task EveryCallNamesTheSourceAppAndTheResource()
    {
        var fetch = new FakeFetch(Answers);
        await Api(fetch).LookupAsync("193.0.0.0/21", CancellationToken.None);
        var queries = fetch.Calls.Select(c => HttpUtility.ParseQueryString(c.Query)).ToList();
        Assert.All(queries, q => Assert.Equal("ptirc-bgpwatch", q["sourceapp"]));
        Assert.Equal("193.0.0.0/21", queries[0]["resource"]);
        Assert.Equal(("AS3333", "193.0.0.0/21"), (queries[2]["resource"], queries[2]["prefix"]));
    }

    [Fact]
    public async Task AnswersAreCachedWithinTheTtl()
    {
        var fetch = new FakeFetch(Answers);
        var now = TimeSpan.Zero;
        var api = Api(fetch, () => now);
        await api.LookupAsync("193.0.6.139", CancellationToken.None);
        await api.LookupAsync("193.0.6.139", CancellationToken.None);
        Assert.Equal(3, fetch.Calls.Count);
        now = RipeStat.Ttl + TimeSpan.FromSeconds(1);
        await api.LookupAsync("193.0.6.139", CancellationToken.None);
        Assert.Equal(6, fetch.Calls.Count);
    }

    [Fact]
    public async Task AnUnannouncedResourceHasNoPrefixAndNoOrigins()
    {
        var answers = new Dictionary<string, string>(Answers) { ["network-info"] = """{"asns": [], "prefix": ""}""" };
        var result = await Api(new FakeFetch(answers)).LookupAsync("203.0.113.9", CancellationToken.None);
        Assert.Equal(("203.0.113.9", (string?)null, false), (result.Resource, result.Prefix, result.Announced));
        Assert.Empty(result.Origins);
    }

    [Fact]
    public async Task MyIpComesFromWhatsMyIp()
    {
        Assert.Equal("2a02:2f04::1", await Api(new FakeFetch(Answers)).MyIpAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheRoasOfAPrefixComeWhateverVerdictTheyGotForTheOrigin()
    {
        var fetch = new FakeFetch(new Dictionary<string, string>(Answers) { ["rpki-validation"] = InvalidAsn });
        var roa = Assert.Single(await Api(fetch).RoasAsync("193.0.0.0/21", 64496, CancellationToken.None));
        Assert.Equal(("193.0.0.0/21", 21, 3333L), (roa.Prefix.ToString(), roa.MaxLength, roa.OriginAs));
        var query = HttpUtility.ParseQueryString(Assert.Single(fetch.Calls).Query);
        Assert.Equal(("AS64496", "193.0.0.0/21"), (query["resource"], query["prefix"]));
    }

    [Fact]
    public async Task TheMoreSpecificsComeWithTheirOrigins()
    {
        var inside = await Api(new FakeFetch(Answers)).MoreSpecificsAsync("193.0.0.0/21", CancellationToken.None);
        Assert.Equal([("193.0.4.0/22", 3333L), ("193.0.6.0/24", 64496L)], inside);
    }

    [Fact]
    public async Task TheLookingGlassGivesOneRoutePerPeerNamedByItsCollector()
    {
        var seenAt = new DateTime(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);
        var routes = await Api(new FakeFetch(Answers)).LookingGlassAsync("193.0.0.0/21", seenAt, CancellationToken.None);
        Assert.Equal(3, routes.Count);
        var first = routes[0];
        Assert.Equal(("rrc01 5.57.80.113", 3333L, "5.57.80.113", "193.0.0.0/21", seenAt), (first.Peer, first.OriginAs, first.NextHop, first.Prefix.ToString(), first.SeenAt));
        Assert.Equal([15692, 174, 1273, 3333], first.AsPath);
        Assert.Equal([3333], routes[1].AsPath);
        Assert.Equal(("rrc00 193.0.0.56", 64497L), (routes[2].Peer, routes[2].OriginAs));
        Assert.Equal([3333, 64496, 64497], routes[2].AsPath);
    }

    [Fact]
    public async Task TheAnnouncedPrefixesOfAnAsAreListed()
    {
        var fetch = new FakeFetch(Answers);
        Assert.Equal(["193.0.0.0/21", "193.0.10.0/23"], await Api(fetch).AnnouncedPrefixesAsync(3333, CancellationToken.None));
        Assert.Equal("AS3333", HttpUtility.ParseQueryString(Assert.Single(fetch.Calls).Query)["resource"]);
    }

    [Fact]
    public async Task AFailedAnswerRaisesTheAdapterError()
    {
        var api = new RipeStat((_, _) => Task.FromResult(JsonNode.Parse("""{"status": "error", "messages": [["error", "nope"]]}""")),
            () => TimeSpan.Zero);
        await Assert.ThrowsAsync<RipeStatException>(() => api.LookupAsync("193.0.6.139", CancellationToken.None));
    }

    [Fact]
    public async Task ACallThatHangsIsCutByTheTimeout()
    {
        var api = new RipeStat(async (_, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return null;
        }, () => TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        var error = await Assert.ThrowsAsync<RipeStatException>(() => api.MyIpAsync(CancellationToken.None));
        Assert.Equal("RIPEstat whats-my-ip: no answer within 0 s", error.Message);
    }

    [Fact]
    public async Task AnUnreachableApiRaisesTheAdapterError()
    {
        var api = new RipeStat((_, _) => throw new HttpRequestException("timed out"), () => TimeSpan.Zero);
        await Assert.ThrowsAsync<RipeStatException>(() => api.LookupAsync("193.0.6.139", CancellationToken.None));
    }

    [Theory]
    [InlineData(" 193.0.6.139 ", "193.0.6.139")]
    [InlineData("193.0.6.139/21", "193.0.0.0/21")]
    [InlineData("2001:DB8::1", "2001:db8::1")]
    public void NormalizeAcceptsAddressesAndPrefixes(string resource, string expected)
    {
        Assert.Equal(expected, RipeStat.Normalize(resource));
    }

    [Theory]
    [InlineData("example.org")]
    [InlineData("193.0.6")]
    [InlineData("010.0.0.1")]
    [InlineData("193.0.6.256")]
    [InlineData("193.0.6.139/33")]
    public void NormalizeRefusesAnythingElse(string resource)
    {
        Assert.Throws<ArgumentException>(() => RipeStat.Normalize(resource));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2a02:2f04::1", true)]
    [InlineData("172.18.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("203.0.113.9", false)]
    [InlineData("fe80::1", false)]
    public void OnlyPublicAddressesAreGlobal(string address, bool global)
    {
        Assert.Equal(global, AddressScope.IsGlobal(System.Net.IPAddress.Parse(address)));
    }
}
