using System.Collections.Concurrent;
using System.Threading.Channels;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Lookup;
using Roa = BGPWatch.Core.Domain.Roa;

namespace BGPWatch.Web.Watch;

/// <summary>
/// The monitor as a whole: the settings in force, the feed, the ROAs in force and when
/// they were last read, the last seed, whether a seed runs, and why a prefix's seed failed.
/// </summary>
public sealed record WatchStatus(WatchSettings Settings, FeedStatus Feed, int Vrps, DateTime? VrpsRefreshedAt, DateTime? SeededAt, bool Seeding,
    IReadOnlyDictionary<string, string> SeedFailures);

/// <summary>
/// The Internet monitor: keeps the RIS Live subscriptions, the detector's ground truth,
/// the incident desk and the webhooks in line with the watch file, and runs every route
/// the stream brings through the pipeline under the source "ris". A prefix new to the
/// watchlist is seeded in the background: the ROAs that cover it and every prefix
/// announced inside it, then the routes RIPEstat's looking glass holds for it and for each
/// prefix inside it announced by an unexpected origin, judged as if the stream had just
/// brought them, so a hijack under way raises its incident at once. The file is applied
/// again when it changes on disk; the incidents are swept every minute, the ROAs read again
/// every refresh period, the stored routes pruned daily. No failure of RIPEstat, the
/// database or the file stops the monitor.
/// </summary>
public sealed class WatchService : BackgroundService
{
    public const string Source = "ris";

    /// <summary>The log category the pipeline's alerts go under; the CLI keeps it quiet, stdout has them.</summary>
    public const string PipelineLog = "BGPWatch.Pipeline";
    public static readonly TimeSpan FilePoll = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan PruneEvery = TimeSpan.FromHours(24);
    private const int BatchLimit = 1000;

    private readonly WatchFile file;
    private readonly IRouteLog log;
    private readonly IncidentNotifier desk;
    private readonly RipeStat ripe;
    private readonly HttpClient webhooks;
    private readonly ILogger logger;
    private readonly Channel<List<Announcement>> queue =
        Channel.CreateUnbounded<List<Announcement>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Pipeline pipeline;
    private readonly RisLive feed;
    private readonly SemaphoreSlim applying = new(1, 1);
    private readonly SemaphoreSlim offering = new(1, 1);
    private readonly SemaphoreSlim background = new(1, 1);
    private readonly ConcurrentDictionary<string, IReadOnlyList<Roa>> roas = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> seedFailures = new(StringComparer.Ordinal);
    private CancellationToken stopping;
    private int seedsRunning;
    private volatile WatchSettings settings = new();
    private DateTime? vrpsRefreshedAt;
    private DateTime? seededAt;
    private DateTime? stamp;

    public WatchService(WatchFile file, IRouteLog log, IncidentNotifier desk, RipeStat ripe, HttpClient webhooks, ILoggerFactory logs)
    {
        this.file = file;
        this.log = log;
        this.desk = desk;
        this.ripe = ripe;
        this.webhooks = webhooks;
        logger = logs.CreateLogger<WatchService>();
        pipeline = new Pipeline(new DetectorConfig([], []), log, logs.CreateLogger(PipelineLog), Source);
        feed = new RisLive(RipeStat.SourceApp, queue.Writer, logs.CreateLogger<RisLive>());
    }

    /// <summary>Told after every batch the pipeline judged: the routes and the alerts they raised.</summary>
    public Action<IReadOnlyList<Announcement>, IReadOnlyList<Alert>>? OnProcessed { get; set; }

    /// <summary>Notifiers kept beside the file's webhooks whatever the file says; the CLI's console is one.</summary>
    public IReadOnlyList<INotifier> AlwaysNotify { get; set; } = [];

    public WatchStatus Status => new(settings, feed.Status, Vrps().Count, vrpsRefreshedAt, seededAt, Volatile.Read(ref seedsRunning) > 0,
        new Dictionary<string, string>(seedFailures, StringComparer.Ordinal));

    protected override Task ExecuteAsync(CancellationToken stop)
    {
        return RunAsync(stop);
    }

    public async Task RunAsync(CancellationToken stop)
    {
        stopping = stop;
        await ApplyAsync(Load() ?? new WatchSettings(), stop);
        try
        {
            await Task.WhenAll(feed.RunAsync(stop), ConsumeAsync(stop), TickAsync(stop), desk.RunAsync(stop));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("monitor stopped");
        }
    }

    /// <summary>
    /// Puts the settings in force at once: the ground truth, the incident desk, the
    /// webhooks, the subscriptions; every prefix new to the watchlist is seeded in the background.
    /// </summary>
    public async Task ApplyAsync(WatchSettings next, CancellationToken cancel)
    {
        await applying.WaitAsync(cancel);
        try
        {
            var before = settings.Monitored.Select(m => m.Prefix).ToHashSet(StringComparer.Ordinal);
            settings = next;
            stamp = file.LastWrite;
            var watched = next.Monitored.Select(m => m.Prefix).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in roas.Keys.Concat(seedFailures.Keys).Where(p => !watched.Contains(p)).ToList())
            {
                roas.TryRemove(gone, out _);
                seedFailures.TryRemove(gone, out _);
            }
            Reconfigure();
            await desk.WatchAsync(Source, pipeline.Config.Monitored.Select(m => m.Prefix), pipeline.Offends, cancel);
            desk.Notifiers = [.. AlwaysNotify, .. WebhooksOf(next, webhooks, logger)];
            feed.Subscribe(Subscriptions(next));
            logger.LogInformation("watching {Prefixes} prefixes, {Notifiers} notifiers", watched.Count, desk.Notifiers.Count);
            var added = next.Monitored.Select(m => m.Prefix).Where(p => !before.Contains(p)).ToList();
            if (added.Count > 0)
            {
                Later(cancelSeed => SeedAsync(added, cancelSeed), seed: true);
            }
        }
        finally
        {
            applying.Release();
        }
    }

    /// <summary>Runs routes through the pipeline and the desk, one batch at a time, as the stream does.</summary>
    public async Task OfferAsync(IReadOnlyList<Announcement> routes, CancellationToken cancel)
    {
        await offering.WaitAsync(cancel);
        try
        {
            var alerts = await pipeline.ProcessAsync(routes, cancel);
            await desk.OfferAsync(Source, routes, alerts, cancel);
            OnProcessed?.Invoke(routes, alerts);
        }
        finally
        {
            offering.Release();
        }
    }

    public static IReadOnlyList<RisSubscription> Subscriptions(WatchSettings settings)
    {
        var hosts = settings.Ris.Hosts.Count == 0 ? [null] : settings.Ris.Hosts.Select(h => (string?)h).ToList();
        return [.. settings.Monitored.SelectMany(m => hosts.Select(h => new RisSubscription(m.Prefix, h)))];
    }

    /// <summary>The file's webhooks as notifiers; one whose URL is not http(s) is logged and left out.</summary>
    public static IReadOnlyList<INotifier> WebhooksOf(WatchSettings settings, HttpClient http, ILogger logger)
    {
        var notifiers = new List<INotifier>();
        foreach (var hook in settings.Webhook)
        {
            if (Uri.TryCreate(hook.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            {
                notifiers.Add(new WebhookNotifier(http, hook.Name, url, logger));
            }
            else
            {
                logger.LogWarning("webhook {Name} skipped: not an http(s) URL", hook.Name);
            }
        }
        return notifiers;
    }

    /// <summary>The watch file, or null when it cannot be read or holds an entry that does not parse; the failure is logged once.</summary>
    private WatchSettings? Load()
    {
        try
        {
            var loaded = file.Load();
            loaded.ToDetector();
            return loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Tomlyn.TomlException or FormatException or ArgumentException)
        {
            stamp = file.LastWrite;
            logger.LogError("watch file {Path} not applied: {Reason}", file.Location, e.Message);
            return null;
        }
    }

    private void Reconfigure()
    {
        pipeline.Reconfigure(new DetectorConfig(settings.ToDetector().Monitored, Vrps()));
    }

    private IReadOnlyList<Roa> Vrps()
    {
        return [.. settings.Vrp.Select(v => v.ToDomain()).Concat(roas.Values.SelectMany(r => r)).Distinct()];
    }

    private bool Watching(string prefix)
    {
        return settings.Monitored.Any(m => m.Prefix == prefix);
    }

    private long[] ExpectedOf(string prefix)
    {
        return settings.Monitored.FirstOrDefault(m => m.Prefix == prefix)?.ExpectedOrigins ?? [];
    }

    /// <summary>Seeds each prefix still watched; a prefix that fails keeps its reason until a seed of it succeeds.</summary>
    private async Task SeedAsync(IReadOnlyList<string> prefixes, CancellationToken cancel)
    {
        foreach (var prefix in prefixes.Where(Watching))
        {
            try
            {
                var inside = await ripe.MoreSpecificsAsync(prefix, cancel);
                await LoadRoasAsync(prefix, inside, cancel);
                var expected = ExpectedOf(prefix);
                var suspicious = inside.Where(m => m.Origin is not { } origin || !expected.Contains(origin)).Select(m => m.Prefix);
                var routes = new List<Announcement>();
                foreach (var target in new[] { prefix }.Concat(suspicious).Distinct())
                {
                    routes.AddRange(await ripe.LookingGlassAsync(target, DateTime.UtcNow, cancel));
                }
                await OfferAsync(routes, cancel);
                seedFailures.TryRemove(prefix, out _);
                logger.LogInformation("seeded {Prefix}: {Routes} routes, {Inside} prefixes announced inside", prefix, routes.Count, inside.Count);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                seedFailures[prefix] = e.Message;
                logger.LogWarning("{Prefix} not seeded: {Reason}", prefix, e.Message);
            }
        }
        seededAt = DateTime.UtcNow;
    }

    /// <summary>Reads the ROAs that cover a watched prefix and each prefix announced inside it, and puts them in force.</summary>
    private async Task LoadRoasAsync(string prefix, IReadOnlyList<(string Prefix, long? Origin)> inside, CancellationToken cancel)
    {
        var origin = ExpectedOf(prefix).FirstOrDefault();
        var found = new List<Roa>(await ripe.RoasAsync(prefix, origin, cancel));
        foreach (var (more, moreOrigin) in inside)
        {
            found.AddRange(await ripe.RoasAsync(more, moreOrigin ?? origin, cancel));
        }
        await applying.WaitAsync(cancel);
        try
        {
            if (Watching(prefix))
            {
                roas[prefix] = [.. found.Distinct()];
                vrpsRefreshedAt = DateTime.UtcNow;
                Reconfigure();
            }
        }
        finally
        {
            applying.Release();
        }
    }

    /// <summary>Reads the ROAs of every watched prefix again; a prefix that fails keeps the ones in force.</summary>
    private async Task RefreshRoasAsync(CancellationToken cancel)
    {
        foreach (var entry in settings.Monitored)
        {
            try
            {
                await LoadRoasAsync(entry.Prefix, await ripe.MoreSpecificsAsync(entry.Prefix, cancel), cancel);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("ROAs of {Prefix} not refreshed: {Reason}", entry.Prefix, e.Message);
            }
        }
        logger.LogInformation("{Count} ROAs in force", Vrps().Count);
    }

    /// <summary>Runs RIPEstat work in the background, one job at a time; a seed shows on the status while it runs.</summary>
    private void Later(Func<CancellationToken, Task> job, bool seed)
    {
        if (seed)
        {
            Interlocked.Increment(ref seedsRunning);
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await background.WaitAsync(stopping);
                try
                {
                    await job(stopping);
                }
                finally
                {
                    background.Release();
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("background work stopped");
            }
            catch (Exception e)
            {
                logger.LogError(e, "background work failed");
            }
            finally
            {
                if (seed)
                {
                    Interlocked.Decrement(ref seedsRunning);
                }
            }
        });
    }

    private async Task ConsumeAsync(CancellationToken stop)
    {
        while (await queue.Reader.WaitToReadAsync(stop))
        {
            var routes = new List<Announcement>();
            while (routes.Count < BatchLimit && queue.Reader.TryRead(out var next))
            {
                routes.AddRange(next);
            }
            try
            {
                await OfferAsync(routes, stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "storing {Count} routes failed", routes.Count);
            }
        }
    }

    private async Task TickAsync(CancellationToken stop)
    {
        var lastSweep = DateTime.UtcNow;
        var lastRefresh = DateTime.UtcNow;
        var lastPrune = DateTime.UtcNow;
        using var timer = new PeriodicTimer(FilePoll);
        while (await timer.WaitForNextTickAsync(stop))
        {
            var now = DateTime.UtcNow;
            try
            {
                if (file.LastWrite != stamp && Load() is { } next)
                {
                    logger.LogInformation("watch file changed, applying");
                    await ApplyAsync(next, stop);
                }
                if (now - lastSweep >= SweepEvery)
                {
                    lastSweep = now;
                    await desk.SweepAsync(now, stop);
                }
                if (now - lastRefresh >= TimeSpan.FromMinutes(Math.Max(1, settings.Rpki.RefreshMinutes)))
                {
                    lastRefresh = now;
                    Later(RefreshRoasAsync, seed: false);
                }
                if (now - lastPrune >= PruneEvery)
                {
                    lastPrune = now;
                    var pruned = await log.PruneAsync(Source, now - TimeSpan.FromDays(settings.History.RetentionDays), stop);
                    logger.LogInformation("pruned {Count} routes older than {Days} days", pruned, settings.History.RetentionDays);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "monitor tick failed");
            }
        }
    }
}
