using BGPWatch.Core.Domain;

namespace BGPWatch.Tests;

public class PrefixHistoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 18, 56, 0, DateTimeKind.Utc);

    private static StoredRoute Row(long id, int seconds, string peer, string prefix, int? origin, int[] path, bool withdrawn = false)
    {
        return new StoredRoute(id, T0.AddSeconds(seconds), peer, prefix, origin, path, null, withdrawn, "lab");
    }

    private static readonly StoredRoute[] Rows =
    [
        Row(1, 0, "10.10.0.10", "203.0.113.0/24", 65001, [65001]),
        Row(2, 0, "10.10.0.13", "203.0.113.0/24", 65001, [65000, 65001]),
        Row(3, 60, "10.10.0.13", "203.0.113.0/24", 65003, [65003]),
        Row(4, 60, "10.10.0.13", "203.0.113.0/25", 65003, [65003]),
        Row(5, 60, "10.10.0.10", "203.0.113.0/24", 65001, [65001]),
        Row(6, 120, "10.10.0.13", "203.0.113.0/25", null, [], withdrawn: true),
        Row(7, 120, "10.10.0.13", "203.0.113.0/24", 65001, [65000, 65001]),
    ];

    [Fact]
    public void CurrentHoldsTheLastUpdatePerPeerAndPrefix()
    {
        var state = PrefixHistory.Current(Rows.Reverse());
        Assert.Equal(
            [
                ("10.10.0.10", "203.0.113.0/24", (int?)65001, false),
                ("10.10.0.13", "203.0.113.0/24", 65001, false),
                ("10.10.0.13", "203.0.113.0/25", null, true),
            ],
            state.Select(s => (s.Peer, s.Prefix, s.OriginAs, s.Withdrawn)));
    }

    [Fact]
    public void ChangesSkipUpdatesThatRepeatTheSameRoute()
    {
        var changed = PrefixHistory.Changes(Rows.Reverse());
        Assert.Equal([120, 120, 60, 60, 0, 0], changed.Select(c => (int)(c.SeenAt - T0).TotalSeconds));
        Assert.All(changed, c => Assert.True(c.Peer != "10.10.0.10" || c.SeenAt == T0));
    }

    [Fact]
    public void AChangeCarriesTheStateBeforeAndAfter()
    {
        var hijack = Assert.Single(PrefixHistory.Changes(Rows), c => c.SeenAt == T0.AddSeconds(60) && c.Prefix == "203.0.113.0/24");
        Assert.Equal("10.10.0.13", hijack.Peer);
        Assert.True(hijack.Before!.Same(new RouteState(65001, [65000, 65001], false)));
        Assert.True(hijack.After.Same(new RouteState(65003, [65003], false)));
    }

    [Fact]
    public void TheFirstSightingHasNoStateBefore()
    {
        var first = PrefixHistory.Changes(Rows)[^1];
        Assert.Null(first.Before);
        Assert.Equal(65001, first.After.OriginAs);
    }
}
