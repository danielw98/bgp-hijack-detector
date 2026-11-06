using BGPWatch.Core.Domain;
using Microsoft.Extensions.Logging;

namespace BGPWatch.Core.Application;

/// <summary>
/// The path every announcement a source observes takes: into route history, through the
/// detector, and its alerts into the log, one batch at a time. The ground truth can be
/// replaced while it runs; the next batch is judged by the new one.
/// </summary>
public sealed class Pipeline(DetectorConfig config, IRouteLog log, ILogger logger, string source)
{
    private volatile DetectorConfig current = config;
    private volatile HijackDetector detector = new(config.Monitored, config.Vrps);

    public string Source => source;

    public DetectorConfig Config => current;

    public long UpdateCount { get; private set; }

    public long AlertCount { get; private set; }

    public async Task StartAsync(CancellationToken cancel)
    {
        await log.SyncMonitoredAsync(current.Monitored, cancel);
        logger.LogInformation("{Source} ready: {Monitored} monitored prefixes, {Vrps} VRPs", source, current.Monitored.Count, current.Vrps.Count);
    }

    public void Reconfigure(DetectorConfig config)
    {
        detector = new HijackDetector(config.Monitored, config.Vrps);
        current = config;
    }

    /// <summary>Whether the announcement would raise an alert under the ground truth in force.</summary>
    public bool Offends(Announcement announcement)
    {
        return detector.Inspect(announcement).Count > 0;
    }

    /// <summary>Stores the announcements and every alert they raise, in one transaction; returns the alerts.</summary>
    public async Task<IReadOnlyList<Alert>> ProcessAsync(IReadOnlyList<Announcement> announcements, CancellationToken cancel)
    {
        var judge = detector;
        var alerts = announcements.SelectMany(judge.Inspect).ToList();
        await log.AppendAsync(announcements, alerts, source, cancel);
        UpdateCount += announcements.Count;
        AlertCount += alerts.Count;
        foreach (var alert in alerts)
        {
            logger.LogWarning("ALERT {Type}: {Detail} (peer {Peer})", alert.Type.Name(), alert.Detail, alert.Peer);
        }
        return alerts;
    }
}
