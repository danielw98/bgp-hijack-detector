namespace BGPWatch.Core.Domain;

/// <summary>A row of route_updates, as the API returns it.</summary>
public sealed record StoredRoute(
    long Id,
    DateTime SeenAt,
    string Peer,
    string Prefix,
    int? OriginAs,
    int[] AsPath,
    string? NextHop,
    bool Withdrawn,
    string Source);

/// <summary>A row of alerts, as the API returns it.</summary>
public sealed record StoredAlert(
    long Id,
    DateTime RaisedAt,
    string AlertType,
    string Prefix,
    int? ObservedOrigin,
    int[] ExpectedOrigins,
    string? CoveringPrefix,
    string Peer,
    int[] AsPath,
    string Detail,
    string Source);

/// <summary>A row of monitored_prefixes, as the API returns it.</summary>
public sealed record StoredMonitored(string Prefix, int[] ExpectedOrigins, string Description);
