using BGPWatch.Core.Domain;

namespace BGPWatch.Core.Application;

/// <summary>The detector's ground truth: monitored prefixes and RPKI payloads.</summary>
public sealed record DetectorConfig(IReadOnlyList<MonitoredPrefix> Monitored, IReadOnlyList<Roa> Vrps);

/// <summary>
/// Where route updates and alerts are kept. Every row names its source: "lab" for the
/// live station, "ris" for the Internet feed, the incident's name for a recorded incident.
/// </summary>
public interface IRouteLog
{
    Task SyncMonitoredAsync(IEnumerable<MonitoredPrefix> monitored, CancellationToken cancel);

    /// <summary>Stores the updates and their alerts in one transaction.</summary>
    Task AppendAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel);

    Task<SourceCounts> CountAsync(string source, CancellationToken cancel);

    /// <summary>Replaces every row of the source with the updates and their alerts, in one transaction.</summary>
    Task ReplaceAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel);

    /// <summary>Deletes the route updates of a source seen before a moment; returns how many went.</summary>
    Task<long> PruneAsync(string source, DateTime before, CancellationToken cancel);
}

public sealed record SourceCounts(long Updates, long Alerts);

/// <summary>A log that keeps nothing: the headless commands run the pipeline over it.</summary>
public sealed class DiscardingRouteLog : IRouteLog
{
    public Task SyncMonitoredAsync(IEnumerable<MonitoredPrefix> monitored, CancellationToken cancel)
    {
        return Task.CompletedTask;
    }

    public Task AppendAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel)
    {
        return Task.CompletedTask;
    }

    public Task<SourceCounts> CountAsync(string source, CancellationToken cancel)
    {
        return Task.FromResult(new SourceCounts(0, 0));
    }

    public Task ReplaceAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel)
    {
        return Task.CompletedTask;
    }

    public Task<long> PruneAsync(string source, DateTime before, CancellationToken cancel)
    {
        return Task.FromResult(0L);
    }
}

/// <summary>The routes of a recorded file, and how many of its records were malformed and dropped.</summary>
public sealed record ArchiveFile(IReadOnlyList<Announcement> Routes, int MalformedRecords);

/// <summary>The routes of a recorded file of BGP updates.</summary>
public interface IUpdateArchive
{
    ArchiveFile Read(string path);
}

public enum NotificationKind
{
    Opened,
    Closed,
    Test,
}

/// <summary>What a notifier delivers: an incident that opened or closed under a source, or a test.</summary>
public sealed record Notification(NotificationKind Kind, string Source, OpenIncident? Incident, DateTime At, string Text)
{
    public static Notification Opened(string source, OpenIncident incident)
    {
        var seen = incident.Peers.Count == 1 ? "one vantage point" : $"{incident.Peers.Count} vantage points";
        return new Notification(NotificationKind.Opened, source, incident, incident.FirstSeen, $"{incident.Detail}; seen by {seen}");
    }

    public static Notification Closed(string source, OpenIncident incident, DateTime at)
    {
        var text = $"{incident.Prefix} by AS{incident.Origin?.ToString() ?? "None"} is over: no vantage point holds an offending route, "
            + $"last alert {incident.LastSeen:yyyy-MM-dd HH:mm:ss} UTC, {incident.Alerts} alerts from {incident.Peers.Count} vantage points";
        return new Notification(NotificationKind.Closed, source, incident, at, text);
    }

    public static Notification Test(DateTime at)
    {
        return new Notification(NotificationKind.Test, "", null, at, "test notification from BGPWatch");
    }
}

/// <summary>A channel notifications go out on; Name identifies it in logs and reports.</summary>
public interface INotifier
{
    string Name { get; }

    Task NotifyAsync(Notification notification, CancellationToken cancel);
}
