using System.Net;

namespace BGPWatch.Core.Domain;

/// <summary>
/// One offending origin against one monitored prefix, seen from one or more vantage
/// points, open until its alerts pause for longer than the gap.
/// </summary>
public sealed record Incident(
    string Source,
    string Prefix,
    long? Origin,
    IReadOnlyList<long> ExpectedOrigins,
    DateTime FirstSeen,
    DateTime LastSeen,
    IReadOnlyList<string> Peers,
    IReadOnlyList<string> AlertTypes,
    IReadOnlyList<string> Prefixes,
    IReadOnlyList<long> AlertIds);

/// <summary>Alerts grouped into incidents.</summary>
public static class Incidents
{
    public static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    public static List<Incident> Group(IEnumerable<StoredAlert> alerts, IEnumerable<string> monitored)
    {
        return Group(alerts, monitored, Gap);
    }

    /// <summary>
    /// Incidents, newest first. <paramref name="monitored"/> names the monitored prefixes,
    /// so the three alert types one announcement can raise land in the same incident.
    /// </summary>
    public static List<Incident> Group(IEnumerable<StoredAlert> alerts, IEnumerable<string> monitored, TimeSpan gap)
    {
        var networks = monitored.Select(IPNetwork.Parse).ToList();
        var incidents = new List<Builder>();
        var open = new Dictionary<(string, string, long?), Builder>();
        foreach (var alert in alerts.OrderBy(a => a.RaisedAt).ThenBy(a => a.Id))
        {
            var key = (alert.Source, MonitoredOf(alert, networks), alert.ObservedOrigin);
            if (!open.TryGetValue(key, out var incident) || alert.RaisedAt - incident.LastSeen > gap)
            {
                incident = new Builder(key, alert.RaisedAt);
                incidents.Add(incident);
                open[key] = incident;
            }
            incident.Extend(alert);
        }
        return incidents.Select(i => i.Build()).OrderByDescending(i => i.FirstSeen).ToList();
    }

    /// <summary>
    /// The monitored prefix the alert belongs to: its covering prefix when it names one,
    /// else the longest monitored prefix containing it, else its own prefix.
    /// </summary>
    private static string MonitoredOf(StoredAlert alert, List<IPNetwork> networks)
    {
        if (!string.IsNullOrEmpty(alert.CoveringPrefix))
        {
            return alert.CoveringPrefix;
        }
        return networks.MostSpecific(IPNetwork.Parse(alert.Prefix))?.ToString() ?? alert.Prefix;
    }

    private sealed class Builder((string Source, string Prefix, long? Origin) key, DateTime firstSeen)
    {
        private readonly SortedSet<long> expectedOrigins = [];
        private readonly SortedSet<string> peers = new(StringComparer.Ordinal);
        private readonly SortedSet<string> alertTypes = new(StringComparer.Ordinal);
        private readonly SortedSet<string> prefixes = new(StringComparer.Ordinal);
        private readonly List<long> alertIds = [];

        private readonly DateTime firstSeen = firstSeen;

        public DateTime LastSeen { get; private set; } = firstSeen;

        public void Extend(StoredAlert alert)
        {
            LastSeen = alert.RaisedAt > LastSeen ? alert.RaisedAt : LastSeen;
            alertIds.Add(alert.Id);
            peers.Add(alert.Peer);
            alertTypes.Add(alert.AlertType);
            prefixes.Add(alert.Prefix);
            expectedOrigins.UnionWith(alert.ExpectedOrigins);
        }

        public Incident Build()
        {
            return new Incident(key.Source, key.Prefix, key.Origin, [.. expectedOrigins], firstSeen, LastSeen,
                [.. peers], [.. alertTypes], [.. prefixes], [.. alertIds]);
        }
    }
}
