using System.Net;

namespace BGPWatch.Core.Domain;

/// <summary>
/// The route every vantage point holds now for the prefixes inside the monitored ones:
/// an announcement replaces the peer's route for its prefix, a withdrawal removes it.
/// A route outside every monitored prefix is not kept.
/// </summary>
public sealed class RouteTable(IEnumerable<IPNetwork> monitored)
{
    private readonly Dictionary<(string Peer, IPNetwork Prefix), Announcement> routes = new();
    private List<IPNetwork> networks = [.. monitored];

    public IReadOnlyList<Announcement> Routes => [.. routes.Values];

    /// <summary>Changes the monitored prefixes; the routes outside the new ones go.</summary>
    public void Rekey(IEnumerable<IPNetwork> monitored)
    {
        networks = [.. monitored];
        foreach (var key in routes.Keys.Where(k => !Inside(k.Prefix)).ToList())
        {
            routes.Remove(key);
        }
    }

    public void Apply(IReadOnlyList<Announcement> announcements)
    {
        foreach (var announcement in announcements)
        {
            var key = (announcement.Peer, announcement.Prefix);
            if (announcement.Withdrawn)
            {
                routes.Remove(key);
            }
            else if (Inside(announcement.Prefix))
            {
                routes[key] = announcement;
            }
        }
    }

    /// <summary>The routes inside a prefix, from every vantage point.</summary>
    public IReadOnlyList<Announcement> Within(IPNetwork prefix)
    {
        return [.. routes.Values.Where(r => prefix.Covers(r.Prefix))];
    }

    private bool Inside(IPNetwork prefix)
    {
        return networks.MostSpecific(prefix) is not null;
    }
}
