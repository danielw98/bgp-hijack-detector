using BGPWatch.Core.Domain;
using Microsoft.Extensions.Logging;

namespace BGPWatch.Core.Application;

/// <summary>
/// The path every observed announcement takes: into route history, through the
/// detector, and its alerts into the log. Shared by the live station and the loader.
/// </summary>
public sealed class Pipeline(DetectorConfig config, IRouteLog log, ILogger logger, string source)
{
    private readonly HijackDetector detector = new(config.Monitored, config.Vrps);

    public string Source => source;

    public long UpdateCount { get; private set; }

    public long AlertCount { get; private set; }

    public async Task StartAsync(CancellationToken cancel)
    {
        await log.SyncMonitoredAsync(config.Monitored, cancel);
        logger.LogInformation("ready: {Monitored} monitored prefixes, {Vrps} VRPs", config.Monitored.Count, config.Vrps.Count);
    }

    /// <summary>Stores the announcements and every alert they raise, in one transaction.</summary>
    public async Task ProcessAsync(IReadOnlyList<Announcement> announcements, CancellationToken cancel)
    {
        var alerts = announcements.SelectMany(detector.Inspect).ToList();
        await log.AppendAsync(announcements, alerts, source, cancel);
        UpdateCount += announcements.Count;
        AlertCount += alerts.Count;
        foreach (var alert in alerts)
        {
            logger.LogWarning("ALERT {Type}: {Detail} (peer {Peer})", alert.Type.Name(), alert.Detail, alert.Peer);
        }
    }
}
