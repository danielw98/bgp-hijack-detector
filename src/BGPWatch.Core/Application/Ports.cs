using BGPWatch.Core.Domain;

namespace BGPWatch.Core.Application;

/// <summary>The detector's ground truth: monitored prefixes and RPKI payloads.</summary>
public sealed record DetectorConfig(IReadOnlyList<MonitoredPrefix> Monitored, IReadOnlyList<Roa> Vrps);

/// <summary>
/// Where route updates and alerts are kept. Every row names its source: "lab" for the
/// live station, the incident's name for a recorded incident.
/// </summary>
public interface IRouteLog
{
    Task SyncMonitoredAsync(IEnumerable<MonitoredPrefix> monitored, CancellationToken cancel);

    /// <summary>Stores the updates and their alerts in one transaction.</summary>
    Task AppendAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel);

    Task<SourceCounts> CountAsync(string source, CancellationToken cancel);

    Task DeleteAsync(string source, CancellationToken cancel);
}

public sealed record SourceCounts(long Updates, long Alerts);

/// <summary>The routes of a recorded file, and how many of its records were malformed and dropped.</summary>
public sealed record ArchiveFile(IReadOnlyList<Announcement> Routes, int MalformedRecords);

/// <summary>The routes of a recorded file of BGP updates.</summary>
public interface IUpdateArchive
{
    ArchiveFile Read(string path);
}
