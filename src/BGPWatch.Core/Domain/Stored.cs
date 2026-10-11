namespace BGPWatch.Core.Domain;

/// <summary>A row of route_updates, as the API returns it.</summary>
public sealed record StoredRoute(
    long Id,
    DateTime SeenAt,
    string Peer,
    string Prefix,
    long? OriginAs,
    long[] AsPath,
    string? NextHop,
    bool Withdrawn,
    string Source);

/// <summary>A row of alerts, as the API returns it.</summary>
public sealed record StoredAlert(
    long Id,
    DateTime RaisedAt,
    string AlertType,
    string Prefix,
    long? ObservedOrigin,
    long[] ExpectedOrigins,
    string? CoveringPrefix,
    string Peer,
    long[] AsPath,
    string Detail,
    string Source);

/// <summary>A row of monitored_prefixes, as the API returns it.</summary>
public sealed record StoredMonitored(string Prefix, long[] ExpectedOrigins, string Description);
