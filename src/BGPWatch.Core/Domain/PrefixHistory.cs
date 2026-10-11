namespace BGPWatch.Core.Domain;

/// <summary>What a vantage point holds for a prefix.</summary>
public sealed record RouteState(long? OriginAs, long[] AsPath, bool Withdrawn)
{
    public static RouteState Of(StoredRoute row)
    {
        return new RouteState(row.OriginAs, row.AsPath, row.Withdrawn);
    }

    public bool Same(RouteState other)
    {
        return OriginAs == other.OriginAs && Withdrawn == other.Withdrawn && AsPath.SequenceEqual(other.AsPath);
    }
}

/// <summary>An update that changed what a vantage point holds; Before is null for the first sighting.</summary>
public sealed record RouteChange(DateTime SeenAt, string Peer, string Prefix, RouteState? Before, RouteState After);

/// <summary>
/// The history of a prefix as the route updates tell it: the current state at every
/// vantage point, and the moments when a vantage point's route changed.
/// Rows may come in any order.
/// </summary>
public static class PrefixHistory
{
    /// <summary>The last update per (peer, prefix), ordered by peer then prefix.</summary>
    public static List<StoredRoute> Current(IEnumerable<StoredRoute> rows)
    {
        var last = new Dictionary<(string Peer, string Prefix), StoredRoute>();
        foreach (var row in InOrder(rows))
        {
            last[(row.Peer, row.Prefix)] = row;
        }
        return last.OrderBy(e => e.Key.Peer, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Prefix, StringComparer.Ordinal)
            .Select(e => e.Value)
            .ToList();
    }

    /// <summary>Every update that changed what a vantage point holds for a prefix, newest first.</summary>
    public static List<RouteChange> Changes(IEnumerable<StoredRoute> rows)
    {
        var seen = new Dictionary<(string, string), RouteState>();
        var changes = new List<RouteChange>();
        foreach (var row in InOrder(rows))
        {
            var key = (row.Peer, row.Prefix);
            var after = RouteState.Of(row);
            var before = seen.GetValueOrDefault(key);
            if (before is null || !before.Same(after))
            {
                changes.Add(new RouteChange(row.SeenAt, row.Peer, row.Prefix, before, after));
            }
            seen[key] = after;
        }
        changes.Reverse();
        return changes;
    }

    private static IEnumerable<StoredRoute> InOrder(IEnumerable<StoredRoute> rows)
    {
        return rows.OrderBy(r => r.SeenAt).ThenBy(r => r.Id);
    }
}
