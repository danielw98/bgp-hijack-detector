using System.Net;

namespace BGPWatch.Core.Domain;

/// <summary>One prefix as a vantage point saw it in a BGP update.</summary>
public sealed record Announcement(
    IPNetwork Prefix,
    long? OriginAs,
    IReadOnlyList<long> AsPath,
    string Peer,
    string? NextHop,
    bool Withdrawn,
    DateTime SeenAt)
{
    public static Announcement Of(IPNetwork prefix, IReadOnlyList<long> asPath, string peer, string? nextHop, DateTime seenAt)
    {
        return new Announcement(prefix, asPath.Count > 0 ? asPath[^1] : null, asPath, peer, nextHop, false, seenAt);
    }

    public static Announcement Withdrawal(IPNetwork prefix, string peer, DateTime seenAt)
    {
        return new Announcement(prefix, null, [], peer, null, true, seenAt);
    }
}

/// <summary>A prefix whose legitimate origin ASes are known in advance.</summary>
public sealed record MonitoredPrefix(IPNetwork Prefix, IReadOnlySet<long> ExpectedOrigins, string Description);

/// <summary>A Validated ROA Payload: prefix, the longest length it authorizes, and origin.</summary>
public sealed record Roa(IPNetwork Prefix, int MaxLength, long OriginAs);

public enum RpkiState
{
    Valid,
    Invalid,
    NotFound,
}

public enum AlertType
{
    OriginHijack,
    SubprefixHijack,
    RpkiInvalid,
}

public sealed record Alert(
    AlertType Type,
    IPNetwork Prefix,
    long? ObservedOrigin,
    string Peer,
    IReadOnlyList<long> AsPath,
    string Detail,
    IReadOnlySet<long> ExpectedOrigins,
    IPNetwork? CoveringPrefix,
    DateTime RaisedAt);

public static class AlertTypes
{
    /// <summary>The name stored in alerts.alert_type and shown by the API.</summary>
    public static string Name(this AlertType type)
    {
        return type switch
        {
            AlertType.OriginHijack => "origin_hijack",
            AlertType.SubprefixHijack => "subprefix_hijack",
            _ => "rpki_invalid",
        };
    }
}
