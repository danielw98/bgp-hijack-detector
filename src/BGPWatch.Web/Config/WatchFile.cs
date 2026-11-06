using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using Tomlyn;

namespace BGPWatch.Web.Config;

/// <summary>The Internet feed: the collectors to listen to, empty for all of them.</summary>
public sealed record RisEntry
{
    public List<string> Hosts { get; init; } = [];
}

/// <summary>How often the ROAs of the watched prefixes are read again from RIPEstat.</summary>
public sealed record RpkiEntry
{
    public int RefreshMinutes { get; init; } = 60;
}

/// <summary>A webhook: where a notification is posted, under the name the logs use.</summary>
public sealed record WebhookEntry(string Name, string Url);

/// <summary>How long the route rows of the Internet feed are kept.</summary>
public sealed record HistoryEntry
{
    public int RetentionDays { get; init; } = 14;
}

/// <summary>
/// The watch file: the prefixes watched on the real Internet with their expected
/// origins, static ROAs for a run without RIPEstat, the feed, the RPKI refresh, the
/// webhooks and the retention.
/// </summary>
public sealed record WatchSettings
{
    public List<MonitoredEntry> Monitored { get; init; } = [];

    public List<VrpEntry> Vrp { get; init; } = [];

    public RisEntry Ris { get; init; } = new();

    public RpkiEntry Rpki { get; init; } = new();

    public List<WebhookEntry> Webhook { get; init; } = [];

    public HistoryEntry History { get; init; } = new();

    /// <summary>The detector's ground truth: the monitored prefixes and the static ROAs.</summary>
    public DetectorConfig ToDetector()
    {
        return new DetectorConfig([.. Monitored.Select(m => m.ToDomain())], [.. Vrp.Select(v => v.ToDomain())]);
    }
}

/// <summary>
/// The watch file on disk, read whole and written whole: a missing file holds nothing,
/// an edit reads it, changes it and replaces it in one move, and edits run one at a time.
/// </summary>
public sealed class WatchFile(string location)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public string Location => location;

    public static WatchSettings Parse(string toml)
    {
        return TomlSerializer.Deserialize<WatchSettings>(toml, TomlFiles.Options) ?? new WatchSettings();
    }

    public static string Format(WatchSettings settings)
    {
        return TomlSerializer.Serialize(settings, TomlFiles.Options);
    }

    public WatchSettings Load()
    {
        return File.Exists(location) ? Parse(File.ReadAllText(location)) : new WatchSettings();
    }

    /// <summary>When the file was last written, or null without a file.</summary>
    public DateTime? LastWrite => File.Exists(location) ? File.GetLastWriteTimeUtc(location) : null;

    /// <summary>Adds a prefix with its expected origins, or replaces the entry of that prefix.</summary>
    public Task<WatchSettings> AddAsync(string prefix, IEnumerable<long> origins, string description, CancellationToken cancel)
    {
        var network = Prefixes.ParseLoose(prefix).ToString();
        var entry = new MonitoredEntry(network, [.. origins.Distinct().Order()], description);
        return EditAsync(settings => settings.Monitored.Any(m => m.Prefix == network)
            ? settings with { Monitored = [.. settings.Monitored.Select(m => m.Prefix == network ? entry : m)] }
            : settings with { Monitored = [.. settings.Monitored, entry] }, cancel);
    }

    /// <summary>Removes the entry of a prefix; the settings come back unchanged when there was none.</summary>
    public Task<WatchSettings> RemoveAsync(string prefix, CancellationToken cancel)
    {
        var network = Prefixes.ParseLoose(prefix).ToString();
        return EditAsync(settings => settings with { Monitored = [.. settings.Monitored.Where(m => m.Prefix != network)] }, cancel);
    }

    public async Task<WatchSettings> EditAsync(Func<WatchSettings, WatchSettings> change, CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            var next = change(Load());
            if (Path.GetDirectoryName(location) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }
            var draft = location + ".tmp";
            await File.WriteAllTextAsync(draft, Format(next), cancel);
            File.Move(draft, location, overwrite: true);
            return next;
        }
        finally
        {
            gate.Release();
        }
    }
}
