using Microsoft.Extensions.Logging;

namespace BGPWatch.Core.Application;

public sealed record LoadSummary(string Source, long Updates, long Alerts, bool Skipped);

/// <summary>
/// Loads the recorded files of an incident through the pipeline under the incident's
/// name, once: a source already stored is left alone unless the load is forced, which
/// replaces it.
/// </summary>
public sealed class IncidentLoader(IRouteLog log, IUpdateArchive archive, ILogger logger)
{
    public async Task<LoadSummary> LoadAsync(DetectorConfig config, string source, IEnumerable<string> files,
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
            await pipeline.ProcessAsync(archive.Read(file), cancel);
        }
        return new LoadSummary(source, pipeline.UpdateCount, pipeline.AlertCount, false);
    }
}
