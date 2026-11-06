using System.Net;

namespace BGPWatch.Core.Domain;

/// <summary>One offending origin against one monitored prefix, as the tracker holds it.</summary>
public sealed record OpenIncident(
    string Prefix,
    long? Origin,
    IReadOnlyList<long> ExpectedOrigins,
    DateTime FirstSeen,
    DateTime LastSeen,
    int Alerts,
    IReadOnlyList<string> Peers,
    IReadOnlyList<string> Prefixes,
    IReadOnlyList<string> AlertTypes,
    string Detail);

public enum IncidentChange
{
    Opened,
    Extended,
    Closed,
}

public sealed record IncidentEvent(IncidentChange Change, OpenIncident Incident);

/// <summary>
/// Alerts arriving as they are raised, grouped into incidents the way Incidents.Group
/// groups the stored ones: one per monitored prefix and offending origin. An incident
/// stays open while its alerts keep coming or a vantage point still holds the route;
/// Sweep closes the ones quiet for longer than the gap whose routes are gone.
/// </summary>
public sealed class IncidentTracker(IEnumerable<IPNetwork> monitored, TimeSpan gap)
{
    private readonly Dictionary<(string Prefix, long? Origin), Builder> open = new();
    private List<IPNetwork> networks = [.. monitored];

    public IncidentTracker(IEnumerable<IPNetwork> monitored)
        : this(monitored, Incidents.Gap)
    {
    }

    /// <summary>The open incidents, newest first.</summary>
    public IReadOnlyList<OpenIncident> Open => [.. open.Values.Select(b => b.Build()).OrderByDescending(i => i.FirstSeen)];

    /// <summary>Changes the monitored prefixes; the incidents already open keep their key.</summary>
    public void Rekey(IEnumerable<IPNetwork> monitored)
    {
        networks = [.. monitored];
    }

    /// <summary>Files the alerts; reports each incident they opened or extended, once.</summary>
    public IReadOnlyList<IncidentEvent> Offer(IReadOnlyList<Alert> alerts)
    {
        var touched = new Dictionary<(string Prefix, long? Origin), IncidentChange>();
        foreach (var alert in alerts.OrderBy(a => a.RaisedAt))
        {
            var key = (Incidents.MonitoredOf(alert.Prefix.ToString(), alert.CoveringPrefix?.ToString(), networks), alert.ObservedOrigin);
            if (!open.TryGetValue(key, out var incident))
            {
                incident = new Builder(key, alert.RaisedAt, alert.Detail);
                open[key] = incident;
                touched[key] = IncidentChange.Opened;
            }
            else
            {
                touched.TryAdd(key, IncidentChange.Extended);
            }
            incident.Extend(alert);
        }
        return [.. touched.Select(t => new IncidentEvent(t.Value, open[t.Key].Build()))];
    }

    /// <summary>
    /// Closes and reports the incidents quiet for longer than the gap whose offending
    /// routes no vantage point holds any more, as <paramref name="persists"/> tells
    /// for a monitored prefix and an origin.
    /// </summary>
    public IReadOnlyList<IncidentEvent> Sweep(DateTime now, Func<string, long?, bool> persists)
    {
        var closing = open.Where(e => now - e.Value.LastSeen > gap && !persists(e.Key.Prefix, e.Key.Origin)).ToList();
        foreach (var (key, _) in closing)
        {
            open.Remove(key);
        }
        return [.. closing.Select(e => new IncidentEvent(IncidentChange.Closed, e.Value.Build()))];
    }

    private sealed class Builder((string Prefix, long? Origin) key, DateTime firstSeen, string detail)
    {
        private readonly SortedSet<long> expectedOrigins = [];
        private readonly SortedSet<string> peers = new(StringComparer.Ordinal);
        private readonly SortedSet<string> alertTypes = new(StringComparer.Ordinal);
        private readonly SortedSet<string> prefixes = new(StringComparer.Ordinal);
        private readonly DateTime firstSeen = firstSeen;
        private int alerts;

        public DateTime LastSeen { get; private set; } = firstSeen;

        public void Extend(Alert alert)
        {
            LastSeen = alert.RaisedAt > LastSeen ? alert.RaisedAt : LastSeen;
            alerts++;
            peers.Add(alert.Peer);
            alertTypes.Add(alert.Type.Name());
            prefixes.Add(alert.Prefix.ToString());
            expectedOrigins.UnionWith(alert.ExpectedOrigins);
        }

        public OpenIncident Build()
        {
            return new OpenIncident(key.Prefix, key.Origin, [.. expectedOrigins], firstSeen, LastSeen, alerts,
                [.. peers], [.. prefixes], [.. alertTypes], detail);
        }
    }
}
