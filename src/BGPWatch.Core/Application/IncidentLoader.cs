using BGPWatch.Core.Domain;
using Microsoft.Extensions.Logging;

namespace BGPWatch.Core.Application;

public sealed record LoadSummary(string Source, long Updates, long Alerts, bool Skipped);

/// <summary>
/// Loads the recorded files of an incident through the detector under the incident's
/// name, once: a source already stored is left alone unless the load is forced. A load
/// replaces the stored rows in one transaction, so a reader sees the old rows or the new,
/// and a load that fails leaves the stored rows as they were. Loads run one at a time.
/// </summary>
public sealed class IncidentLoader(IRouteLog log, IUpdateArchive archive, ILogger logger)
{
    private readonly SemaphoreSlim oneAtATime = new(1, 1);

    public async Task<LoadSummary> LoadAsync(DetectorConfig config, string source, IEnumerable<string> files,
        bool force, CancellationToken cancel)
    {
        await oneAtATime.WaitAsync(cancel);
        try
        {
            return await LoadAloneAsync(config, source, files, force, cancel);
        }
        finally
        {
            oneAtATime.Release();
        }
    }

    private async Task<LoadSummary> LoadAloneAsync(DetectorConfig config, string source, IEnumerable<string> files,
        bool force, CancellationToken cancel)
    {
        await log.SyncMonitoredAsync(config.Monitored, cancel);
        var stored = await log.CountAsync(source, cancel);
        if (stored.Updates > 0 && !force)
        {
            return new LoadSummary(source, stored.Updates, stored.Alerts, true);
        }
        var updates = files.SelectMany(file => Read(source, file)).ToList();
        var detector = new HijackDetector(config.Monitored, config.Vrps);
        var alerts = updates.SelectMany(detector.Inspect).ToList();
        await log.ReplaceAsync(updates, alerts, source, cancel);
        return new LoadSummary(source, updates.Count, alerts.Count, false);
    }

    private IReadOnlyList<Announcement> Read(string source, string file)
    {
        var recorded = archive.Read(file);
        if (recorded.MalformedRecords > 0)
        {
            logger.LogWarning("{Source}: {File} has {Count} malformed records, dropped", source, file, recorded.MalformedRecords);
        }
        return recorded.Routes;
    }
}
