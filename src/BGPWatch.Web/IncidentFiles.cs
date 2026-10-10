using BGPWatch.Core.Application;
using BGPWatch.Web.Config;

namespace BGPWatch.Web;

/// <summary>
/// A recorded incident on disk: config.toml (ground truth, documented timeline) and the
/// updates.* MRT files beside it, stored under the folder's name as their source.
/// </summary>
public static class IncidentFiles
{
    public static Task<LoadSummary> LoadAsync(IncidentLoader loader, Settings settings, string name, bool force, CancellationToken cancel)
    {
        var folder = settings.IncidentDir(name) ?? throw new IOException($"unknown incident {name}");
        var files = Directory.GetFiles(folder, "updates.*").Order(StringComparer.Ordinal);
        var config = TomlFiles.LoadDetector(Path.Combine(folder, "config.toml"));
        return loader.LoadAsync(config, name, files, force, cancel);
    }
}

/// <summary>Loads every recorded incident once at start; an incident already stored is left alone.</summary>
public sealed class IncidentStartup(IncidentLoader loader, Settings settings, ILogger<IncidentStartup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        foreach (var name in settings.IncidentNames())
        {
            var summary = await IncidentFiles.LoadAsync(loader, settings, name, force: false, stop);
            logger.LogInformation("incident {Source}: {Updates} updates, {Alerts} alerts, {State}",
                summary.Source, summary.Updates, summary.Alerts, summary.Skipped ? "already stored" : "loaded");
        }
    }
}
