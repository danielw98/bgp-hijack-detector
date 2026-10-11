using Microsoft.Extensions.Logging;

namespace BGPWatch.Core.Application;

public sealed record LoadSummary(string Source, long Updates, long Alerts, bool Skipped);

/// <summary>
/// Loads the recorded files of an incident through the pipeline under the incident's
/// name, once: a source already stored is left alone unless the load is forced, which
/// replaces it. Loads run one at a time, so a reload never interleaves with another load.
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
        var pipeline = new Pipeline(config, log, logger, source);
        await pipeline.StartAsync(cancel);
        var stored = await log.CountAsync(source, cancel);
        if (stored.Updates > 0 && !force)
        {
            return new LoadSummary(source, stored.Updates, stored.Alerts, true);
        }
        if (stored.Updates > 0)
        {
            await log.DeleteAsync(source, cancel);
        }
        foreach (var file in files)
        {
            var recorded = archive.Read(file);
            if (recorded.MalformedRecords > 0)
            {
                logger.LogWarning("{Source}: {File} has {Count} malformed records, dropped", source, file, recorded.MalformedRecords);
            }
            await pipeline.ProcessAsync(recorded.Routes, cancel);
        }
        return new LoadSummary(source, pipeline.UpdateCount, pipeline.AlertCount, false);
    }
}
