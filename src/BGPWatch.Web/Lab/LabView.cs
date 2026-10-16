using System.Text;
using System.Text.Json;

namespace BGPWatch.Web.Lab;

/// <summary>A lab router; Hosts are the addresses of the monitored prefix it answers for itself (its loopback).</summary>
public sealed record Router(string Role, string Container, string Address, long Asn, string[] Hosts);

public sealed record Session(string Peer, long RemoteAs, bool Established);

/// <summary>A path of a router's BGP table; NextHop is null for a route the router originates.</summary>
public sealed record LabPath(string Prefix, long[] AsPath, long OriginAs, string? NextHop, bool Best, string Reason);

/// <summary>A route a router received from the attacker and refused under route-map ROV.</summary>
public sealed record RejectedRoute(string Prefix, long[] AsPath, long OriginAs);

/// <summary>
/// A router as it reports itself; Reporting tells whether its BMP session to the station is up,
/// Rov whether route-map ROV filters the routes of one of its neighbors (origin validation),
/// Rejected the attacker's routes that ROV refused.
/// </summary>
public sealed record RouterState(
    string Role,
    string Address,
    long Asn,
    string[] Hosts,
    bool Reachable,
    bool Reporting,
    bool Rov,
    List<Session> Sessions,
    List<LabPath> Paths,
    List<RejectedRoute> Rejected);

/// <summary>The station the routers report to over BMP.</summary>
public sealed record Collector(string Address, int Port);

/// <summary>
/// The lab as its routers report it: the BGP sessions and the BGP table of each FRR
/// router, read with vtysh and reduced to what the page draws and simulates.
/// Routers mirrors lab/docker-compose.yml and lab/frr/*/frr.conf.
/// </summary>
public static class LabView
{
    /// <summary>The attacker's address: the transit's session that route-map ROV filters.</summary>
    public const string FilteredPeer = "10.10.0.13";

    /// <summary>The route-map of lab/frr/transit/frr.conf that drops the routes RPKI finds invalid.</summary>
    public const string RovRouteMap = "ROV";

    public static readonly string[] ShowCommand =
    [
        "vtysh", "-c", "show bgp ipv4 unicast summary json", "-c", "show ip bgp json", "-c", "show bgp neighbors json",
        "-c", $"show bgp ipv4 unicast neighbors {FilteredPeer} received-routes json",
    ];

    public static readonly Collector Station = new("10.10.0.100", Bmp.BmpStation.Port);

    public static readonly Router[] Routers =
    [
        new("victim", "lab-victim", "10.10.0.11", 65001, ["203.0.113.1", "203.0.113.129"]),
        new("transit", "lab-transit", "10.10.0.10", 65000, []),
        new("attacker", "lab-attacker", "10.10.0.13", 65003, []),
    ];

    /// <summary>
    /// The state of every lab router, all asked at once. <paramref name="run"/> executes a command in a
    /// container and returns its stdout; <paramref name="reporting"/> names the routers
    /// whose BMP session is up.
    /// </summary>
    public static async Task<List<RouterState>> SnapshotAsync(Func<string, IReadOnlyList<string>, Task<string>> run,
        IReadOnlyCollection<string> reporting)
    {
        var states = await Task.WhenAll(Routers.Select(router => StateAsync(router, run, reporting.Contains(router.Address))));
        return [.. states];
    }

    /// <summary>The four JSON documents ShowCommand prints back to back: summary, table, neighbors, received routes.</summary>
    public static (JsonElement Summary, JsonElement Table, JsonElement Neighbors, JsonElement Received) SplitDocuments(string stdout)
    {
        var bytes = Encoding.UTF8.GetBytes(stdout);
        var start = Array.IndexOf(bytes, (byte)'{');
        if (start < 0)
        {
            throw new FormatException("no JSON document in the output");
        }
        var reader = new Utf8JsonReader(bytes.AsSpan(start), new JsonReaderOptions { AllowMultipleValues = true });
        var documents = new List<JsonElement>();
        do
        {
            using var document = JsonDocument.ParseValue(ref reader);
            documents.Add(document.RootElement.Clone());
        }
        while (documents.Count < 4 && reader.Read());
        if (documents.Count < 4)
        {
            throw new FormatException($"{documents.Count} JSON documents in the output, four expected");
        }
        return (documents[0], documents[1], documents[2], documents[3]);
    }

    /// <summary>Whether route-map ROV filters what one of the router's neighbors announces.</summary>
    public static bool RovApplied(JsonElement neighbors)
    {
        return neighbors.ValueKind == JsonValueKind.Object && neighbors.EnumerateObject().Any(n =>
            n.Value.ValueKind == JsonValueKind.Object
            && n.Value.TryGetProperty("addressFamilyInfo", out var families)
            && families.TryGetProperty("ipv4Unicast", out var unicast)
            && unicast.TryGetProperty("routeMapForIncomingAdvertisements", out var map)
            && map.GetString() == RovRouteMap);
    }

    /// <summary>
    /// The routes FilteredPeer sent that are missing from the router's table while ROV is
    /// applied. <paramref name="received"/> is its Adj-RIB-In before policy (soft-reconfiguration
    /// inbound); without ROV nothing counts as rejected.
    /// </summary>
    public static List<RejectedRoute> Rejected(JsonElement received, IReadOnlyList<LabPath> paths, bool rov)
    {
        if (!rov || received.ValueKind != JsonValueKind.Object || !received.TryGetProperty("receivedRoutes", out var routes))
        {
            return [];
        }
        var accepted = paths.Where(p => p.NextHop == FilteredPeer).Select(p => (p.Prefix, string.Join(' ', p.AsPath))).ToHashSet();
        return routes.EnumerateObject()
            .Select(r => (r.Name, Path: r.Value.GetProperty("path").GetString() ?? ""))
            .Where(r => !accepted.Contains((r.Name, r.Path)))
            .Select(r =>
            {
                var asPath = r.Path.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray();
                return new RejectedRoute(r.Name, asPath, asPath.Length > 0 ? asPath[^1] : 0);
            })
            .ToList();
    }

    /// <summary>One entry per configured BGP neighbor of the router.</summary>
    public static List<Session> Sessions(JsonElement summary)
    {
        return summary.GetProperty("peers").EnumerateObject()
            .Select(p => new Session(p.Name, p.Value.GetProperty("remoteAs").GetInt64(), p.Value.GetProperty("state").GetString() == "Established"))
            .ToList();
    }

    /// <summary>Every path in the router's BGP table, the chosen path of each prefix first.</summary>
    public static List<LabPath> Paths(JsonElement table)
    {
        if (table.ValueKind != JsonValueKind.Object || !table.TryGetProperty("routes", out var routes))
        {
            return [];
        }
        var localAs = table.GetProperty("localAS").GetInt64();
        return routes.EnumerateObject()
            .SelectMany(prefix => prefix.Value.EnumerateArray()
                .OrderBy(IsSpare)
                .Select(entry => PathOf(prefix.Name, entry, localAs)))
            .ToList();
    }

    private static async Task<RouterState> StateAsync(Router router, Func<string, IReadOnlyList<string>, Task<string>> run, bool reporting)
    {
        try
        {
            var (summary, table, neighbors, received) = SplitDocuments(await run(router.Container, ShowCommand));
            var rov = RovApplied(neighbors);
            var paths = Paths(table);
            return new RouterState(router.Role, router.Address, router.Asn, router.Hosts, true, reporting, rov,
                Sessions(summary), paths, Rejected(received, paths, rov));
        }
        catch (Exception)
        {
            return new RouterState(router.Role, router.Address, router.Asn, router.Hosts, false, reporting, false, [], [], []);
        }
    }

    private static LabPath PathOf(string prefix, JsonElement entry, long localAs)
    {
        var asPath = entry.GetProperty("path").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray();
        var reason = entry.TryGetProperty("selectionReason", out var why) ? why.GetString() ?? "" : "";
        return new LabPath(prefix, asPath, asPath.Length > 0 ? asPath[^1] : localAs, NextHop(entry), !IsSpare(entry), reason);
    }

    private static bool IsSpare(JsonElement entry)
    {
        return !(entry.TryGetProperty("bestpath", out var best) && best.GetBoolean());
    }

    private static string? NextHop(JsonElement entry)
    {
        if (!entry.TryGetProperty("nexthops", out var hops) || hops.GetArrayLength() == 0)
        {
            return null;
        }
        var address = hops[0].TryGetProperty("ip", out var ip) ? ip.GetString() ?? "" : "";
        return address is "" or "0.0.0.0" ? null : address;
    }
}
