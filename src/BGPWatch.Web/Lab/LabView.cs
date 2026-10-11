using System.Text;
using System.Text.Json;

namespace BGPWatch.Web.Lab;

/// <summary>A lab router; Hosts are the addresses of the monitored prefix it answers for itself (its loopback).</summary>
public sealed record Router(string Role, string Container, string Address, long Asn, string[] Hosts);

public sealed record Session(string Peer, long RemoteAs, bool Established);

/// <summary>A path of a router's BGP table; NextHop is null for a route the router originates.</summary>
public sealed record LabPath(string Prefix, long[] AsPath, long OriginAs, string? NextHop, bool Best, string Reason);

/// <summary>A router as it reports itself; Reporting tells whether its BMP session to the station is up.</summary>
public sealed record RouterState(
    string Role,
    string Address,
    long Asn,
    string[] Hosts,
    bool Reachable,
    bool Reporting,
    List<Session> Sessions,
    List<LabPath> Paths);

/// <summary>The station the routers report to over BMP.</summary>
public sealed record Collector(string Address, int Port);

/// <summary>
/// The lab as its routers report it: the BGP sessions and the BGP table of each FRR
/// router, read with vtysh and reduced to what the page draws and simulates.
/// Routers mirrors lab/docker-compose.yml and lab/frr/*/frr.conf.
/// </summary>
public static class LabView
{
    public static readonly string[] ShowCommand = ["vtysh", "-c", "show bgp ipv4 unicast summary json", "-c", "show ip bgp json"];

    public static readonly Collector Station = new("10.10.0.100", Bmp.BmpStation.Port);

    public static readonly Router[] Routers =
    [
        new("victim", "lab-victim", "10.10.0.11", 65001, ["203.0.113.1", "203.0.113.129"]),
        new("transit", "lab-transit", "10.10.0.10", 65000, []),
        new("attacker", "lab-attacker", "10.10.0.13", 65003, []),
    ];

    /// <summary>
    /// The state of every lab router. <paramref name="run"/> executes a command in a
    /// container and returns its stdout; <paramref name="reporting"/> names the routers
    /// whose BMP session is up.
    /// </summary>
    public static async Task<List<RouterState>> SnapshotAsync(Func<string, IReadOnlyList<string>, Task<string>> run,
        IReadOnlyCollection<string> reporting)
    {
        var states = new List<RouterState>();
        foreach (var router in Routers)
        {
            states.Add(await StateAsync(router, run, reporting.Contains(router.Address)));
        }
        return states;
    }

    /// <summary>The two JSON documents ShowCommand prints back to back: summary, then table.</summary>
    public static (JsonElement Summary, JsonElement Table) SplitDocuments(string stdout)
    {
        var bytes = Encoding.UTF8.GetBytes(stdout);
        var start = Array.IndexOf(bytes, (byte)'{');
        if (start < 0)
        {
            throw new FormatException("no JSON document in the output");
        }
        var reader = new Utf8JsonReader(bytes.AsSpan(start), new JsonReaderOptions { AllowMultipleValues = true });
        using var summary = JsonDocument.ParseValue(ref reader);
        if (!reader.Read())
        {
            throw new FormatException("one JSON document in the output, two expected");
        }
        using var table = JsonDocument.ParseValue(ref reader);
        return (summary.RootElement.Clone(), table.RootElement.Clone());
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
            var (summary, table) = SplitDocuments(await run(router.Container, ShowCommand));
            return new RouterState(router.Role, router.Address, router.Asn, router.Hosts, true, reporting, Sessions(summary), Paths(table));
        }
        catch (Exception)
        {
            return new RouterState(router.Role, router.Address, router.Asn, router.Hosts, false, reporting, [], []);
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
