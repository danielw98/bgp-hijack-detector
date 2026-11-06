using System.Net;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;

namespace BGPWatch.Web.Watch;

/// <summary>One origin seen for a watched prefix: by how many vantage points, for which prefixes inside it.</summary>
public sealed record SeenOrigin(long? Asn, bool Expected, int Peers, IReadOnlyList<string> Prefixes);

/// <summary>
/// A watched prefix as the page shows it: its status, the origins the vantage points
/// hold now, and why its seed failed when it did.
/// </summary>
public sealed record PrefixView(
    string Prefix,
    IReadOnlyList<long> ExpectedOrigins,
    string Description,
    string Status,
    IReadOnlyList<SeenOrigin> Origins,
    DateTime? LastUpdate,
    IReadOnlyList<OpenIncident> Incidents,
    string? SeedFailure);

/// <summary>The watched prefixes with what the route table and the open incidents say about each.</summary>
public static class WatchView
{
    public const string Ok = "ok";
    public const string Alert = "alert";
    public const string Unknown = "unknown";

    public static List<PrefixView> Of(IReadOnlyList<MonitoredEntry> monitored, IReadOnlyList<Announcement> routes, IReadOnlyList<OpenIncident> open,
        IReadOnlyDictionary<string, string>? seedFailures = null)
    {
        var networks = monitored.Select(m => IPNetwork.Parse(m.Prefix)).ToList();
        var governed = routes.Where(r => !r.Withdrawn).ToLookup(r => networks.MostSpecific(r.Prefix));
        return [.. monitored.Select(entry => View(entry, governed[IPNetwork.Parse(entry.Prefix)].ToList(),
            open.Where(i => i.Prefix == entry.Prefix).ToList(), seedFailures?.GetValueOrDefault(entry.Prefix)))];
    }

    private static PrefixView View(MonitoredEntry entry, List<Announcement> inside, List<OpenIncident> incidents, string? seedFailure)
    {
        var expected = entry.ExpectedOrigins.ToHashSet();
        var origins = inside.GroupBy(r => r.OriginAs)
            .Select(g => new SeenOrigin(g.Key, g.Key is { } asn && expected.Contains(asn), g.Select(r => r.Peer).Distinct().Count(),
                [.. g.Select(r => r.Prefix.ToString()).Distinct().Order(StringComparer.Ordinal)]))
            .OrderByDescending(o => o.Peers)
            .ToList();
        var status = incidents.Count > 0 || origins.Any(o => !o.Expected) ? Alert : origins.Count > 0 ? Ok : Unknown;
        return new PrefixView(entry.Prefix, [.. entry.ExpectedOrigins.Order()], entry.Description, status, origins,
            inside.Count > 0 ? inside.Max(r => r.SeenAt) : null, incidents, seedFailure);
    }
}
