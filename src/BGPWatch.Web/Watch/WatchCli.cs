using System.Diagnostics;
using System.Text.Json;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Lookup;
using BGPWatch.Web.Native;

namespace BGPWatch.Web.Watch;

/// <summary>The verb and what followed it: the words that stand alone, and the --name values and flags.</summary>
public sealed record CliArgs(string Verb, string[] Words, IReadOnlyDictionary<string, string> Options)
{
    private static readonly HashSet<string> Flags = ["json"];

    public static CliArgs Parse(string[] args)
    {
        var words = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                words.Add(args[i]);
            }
            else if (Flags.Contains(args[i][2..]))
            {
                options[args[i][2..]] = "true";
            }
            else if (i + 1 < args.Length)
            {
                options[args[i][2..]] = args[++i];
            }
            else
            {
                throw new FormatException($"{args[i]} needs a value");
            }
        }
        return new CliArgs(args.Length > 0 ? args[0] : "", [.. words], options);
    }

    public bool Json => Options.ContainsKey("json");
}

/// <summary>
/// The operator's verbs over the watch file, with no database: watch the prefixes on RIS
/// Live, check one against RIPEstat today, replay MRT files through the detector, list,
/// add and remove prefixes, send a test notification. Alerts and incidents go to the
/// output as lines of text or, with --json, one JSON object each; the logs go to stderr.
/// Exit codes: 0, 1 for a usage error or a failure, 2 when check or replay found something.
/// </summary>
public static class WatchCli
{
    public const int Failure = 1;
    public const int Found = 2;
    public const string ReplaySource = "replay";
    public static readonly string[] Verbs = ["watch", "check", "replay", "prefixes", "notify", "help", "--help", "-h"];

    public const string Help = """
        usage: bgpwatch <verb> [options]

          watch     [--config FILE] [--json]            watch the file's prefixes on RIS Live; alerts and
                                                        incidents to stdout (UTC), webhooks on; Ctrl+C stops
          check     PREFIX [--config FILE]              what RIPEstat sees for the prefix today, against the
                                                        file; exit 2 on an unexpected origin
          replay    FILE... [--config FILE] [--json]    the detector over MRT files (.gz, .bz2 or plain);
                                                        exit 2 when it raised anything
          prefixes  list | add PREFIX ASN... [--description TEXT] | remove PREFIX   [--config FILE]
          notify    test [--config FILE]                one test notification per webhook; exit 1 on a failure
          dump      FILE                                every route of an MRT file, one JSON line each
          measure   CONFIG FILE                         what the detector raises on an MRT file, and how long it takes

        FILE defaults to WATCH_CONFIG, then watch/bgpwatch.toml.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter errors)
    {
        CliArgs parsed;
        try
        {
            parsed = CliArgs.Parse(args);
        }
        catch (FormatException e)
        {
            return Fail(errors, e.Message);
        }
        var file = new WatchFile(parsed.Options.GetValueOrDefault("config")
            ?? Environment.GetEnvironmentVariable("WATCH_CONFIG")
            ?? Path.Combine("watch", "bgpwatch.toml"));
        try
        {
            return parsed switch
            {
                { Verb: "watch", Words.Length: 0 } => await WatchAsync(file, parsed.Json, output, errors),
                { Verb: "check", Words: [var prefix] } => await CheckAsync(file, prefix, output),
                { Verb: "replay", Words.Length: > 0 } => await ReplayAsync(file, parsed.Words, parsed.Json, output, errors),
                { Verb: "prefixes" } => await PrefixesAsync(file, parsed, output, errors),
                { Verb: "notify", Words: ["test"] } => await NotifyTestAsync(file, output, errors),
                _ => Usage(errors),
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IOException or Tomlyn.TomlException or RipeStatException)
        {
            return Fail(errors, e.Message);
        }
    }

    private static async Task<int> WatchAsync(WatchFile file, bool json, TextWriter output, TextWriter errors)
    {
        using var logs = Logs();
        var desk = new IncidentNotifier(logs.CreateLogger("BGPWatch.Watch"));
        var service = new WatchService(file, new DiscardingRouteLog(), desk, Ripe(), new HttpClient { Timeout = WebhookNotifier.Timeout }, logs)
        {
            AlwaysNotify = [new ConsoleNotifier(json, output)],
            OnProcessed = (_, alerts) => PrintAlerts(alerts, json, output),
        };
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        errors.WriteLine($"watching the prefixes of {file.Location}; Ctrl+C stops");
        await service.RunAsync(stop.Token);
        return 0;
    }

    /// <summary>What RIPEstat sees for a prefix today, and whether the file expects those origins.</summary>
    private static async Task<int> CheckAsync(WatchFile file, string prefix, TextWriter output)
    {
        var lookup = await Ripe().LookupAsync(prefix, CancellationToken.None);
        if (lookup.Prefix is null || lookup.Origins.Count == 0)
        {
            output.WriteLine($"{lookup.Resource}: not announced, RIPEstat sees no covering prefix");
            return 0;
        }
        output.WriteLine($"{lookup.Resource}: covered by {lookup.Prefix}, {(lookup.Announced ? "announced" : "not announced")}");
        foreach (var origin in lookup.Origins)
        {
            output.WriteLine($"  AS{origin.Asn} {origin.Holder}, RPKI {origin.Rpki.Status}");
        }
        var monitored = file.Load().ToDetector().Monitored;
        var network = Prefixes.ParseLoose(lookup.Prefix);
        if (monitored.Select(m => m.Prefix).MostSpecific(network) is not { } governing)
        {
            output.WriteLine($"not in {file.Location}");
            return 0;
        }
        var expected = monitored.Single(m => m.Prefix == governing).ExpectedOrigins;
        var unexpected = lookup.Origins.Where(o => !expected.Contains(o.Asn)).ToList();
        var origins = string.Join(", ", expected.Order().Select(a => $"AS{a}"));
        if (unexpected.Count == 0)
        {
            output.WriteLine($"ok: {governing} expects {origins}, and that is what RIPEstat sees");
            return 0;
        }
        output.WriteLine($"UNEXPECTED: {governing} expects {origins}, RIPEstat sees {string.Join(", ", unexpected.Select(o => $"AS{o.Asn}"))}");
        return Found;
    }

    /// <summary>The detector over recorded files, under the file's ground truth; the incidents are swept once the files end.</summary>
    private static async Task<int> ReplayAsync(WatchFile file, IReadOnlyList<string> paths, bool json, TextWriter output, TextWriter errors)
    {
        using var logs = Logs();
        var logger = logs.CreateLogger("BGPWatch.Replay");
        var settings = file.Load();
        var config = settings.ToDetector();
        var pipeline = new Pipeline(config, new DiscardingRouteLog(), logs.CreateLogger(WatchService.PipelineLog), ReplaySource);
        var desk = new IncidentNotifier(logger)
        {
            Notifiers = [new ConsoleNotifier(json, output), .. WatchService.WebhooksOf(settings, new HttpClient { Timeout = WebhookNotifier.Timeout }, logger)],
        };
        await desk.WatchAsync(ReplaySource, config.Monitored.Select(m => m.Prefix), pipeline.Offends, CancellationToken.None);
        var clock = Stopwatch.StartNew();
        var byType = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var malformed = 0;
        var last = DateTime.MinValue;
        foreach (var path in paths)
        {
            var archive = new MrtArchive().Read(path);
            malformed += archive.MalformedRecords;
            var alerts = await pipeline.ProcessAsync(archive.Routes, CancellationToken.None);
            PrintAlerts(alerts, json, output);
            await desk.OfferAsync(ReplaySource, archive.Routes, alerts, CancellationToken.None);
            foreach (var alert in alerts)
            {
                byType[alert.Type.Name()] = byType.GetValueOrDefault(alert.Type.Name()) + 1;
            }
            last = archive.Routes.Count > 0 ? archive.Routes.Max(r => r.SeenAt) : last;
        }
        await desk.SweepAsync(last + Incidents.Gap + TimeSpan.FromSeconds(1), CancellationToken.None);
        var types = byType.Count == 0 ? "none" : string.Join(", ", byType.Select(t => $"{t.Value} {t.Key}"));
        errors.WriteLine($"{paths.Count} files, {pipeline.UpdateCount} updates, {malformed} malformed records dropped, "
            + $"{pipeline.AlertCount} alerts ({types}), {desk.Open(ReplaySource).Count} incidents still open, {clock.Elapsed.TotalSeconds:0.00} s");
        return pipeline.AlertCount > 0 ? Found : 0;
    }

    private static async Task<int> PrefixesAsync(WatchFile file, CliArgs parsed, TextWriter output, TextWriter errors)
    {
        switch (parsed.Words)
        {
            case ["list"] or []:
                PrintPrefixes(file, file.Load(), output);
                return 0;
            case ["add", var prefix, .. var asns] when asns.Length > 0:
                var origins = asns.Select(AsnOf).ToList();
                PrintPrefixes(file, await file.AddAsync(prefix, origins, parsed.Options.GetValueOrDefault("description") ?? "", CancellationToken.None), output);
                return 0;
            case ["remove", var prefix]:
                var before = file.Load().Monitored.Count;
                var after = await file.RemoveAsync(prefix, CancellationToken.None);
                output.WriteLine(after.Monitored.Count < before ? $"removed {Prefixes.ParseLoose(prefix)}" : $"{Prefixes.ParseLoose(prefix)} was not in the file");
                PrintPrefixes(file, after, output);
                return 0;
            default:
                return Usage(errors);
        }
    }

    private static async Task<int> NotifyTestAsync(WatchFile file, TextWriter output, TextWriter errors)
    {
        using var logs = Logs();
        var logger = logs.CreateLogger("BGPWatch.Notify");
        var settings = file.Load();
        var desk = new IncidentNotifier(logger)
        {
            Notifiers = WatchService.WebhooksOf(settings, new HttpClient { Timeout = WebhookNotifier.Timeout }, logger),
        };
        if (desk.Notifiers.Count == 0)
        {
            return Fail(errors, $"no webhook in {file.Location}");
        }
        var failed = await desk.TestAsync(DateTime.UtcNow, CancellationToken.None);
        foreach (var notifier in desk.Notifiers)
        {
            output.WriteLine($"{notifier.Name}: {(failed.Contains(notifier.Name) ? "FAILED" : "delivered")}");
        }
        return failed.Count == 0 ? 0 : Failure;
    }

    private static void PrintPrefixes(WatchFile file, WatchSettings settings, TextWriter output)
    {
        output.WriteLine($"{file.Location}: {settings.Monitored.Count} prefixes, {settings.Webhook.Count} webhooks");
        foreach (var entry in settings.Monitored)
        {
            var origins = entry.ExpectedOrigins.Length == 0 ? "no expected origin" : string.Join(", ", entry.ExpectedOrigins.Select(a => $"AS{a}"));
            output.WriteLine($"  {entry.Prefix,-24} {origins}{(entry.Description.Length > 0 ? $"  {entry.Description}" : "")}");
        }
    }

    private static void PrintAlerts(IReadOnlyList<Alert> alerts, bool json, TextWriter output)
    {
        foreach (var alert in alerts)
        {
            output.WriteLine(json
                ? JsonSerializer.Serialize(AlertLine.Of(alert), NotificationJson.Options)
                : $"{alert.RaisedAt:HH:mm:ss} ALERT {alert.Type.Name()} {alert.Detail}, at {alert.Peer}");
        }
    }

    private static long AsnOf(string text)
    {
        return long.TryParse(text.StartsWith("AS", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, out var asn) && asn >= 0
            ? asn
            : throw new FormatException($"not an AS number: {text}");
    }

    private static RipeStat Ripe()
    {
        var started = Stopwatch.GetTimestamp();
        return new RipeStat(RipeStat.Over(new HttpClient()), () => Stopwatch.GetElapsedTime(started));
    }

    /// <summary>Logs on stderr, so the output holds only the alerts and the incidents; the pipeline's own alert log stays quiet.</summary>
    private static ILoggerFactory Logs()
    {
        return LoggerFactory.Create(logging =>
        {
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.UseUtcTimestamp = true;
            });
            logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            logging.AddFilter(WatchService.PipelineLog, LogLevel.Error);
        });
    }

    private static int Usage(TextWriter errors)
    {
        errors.WriteLine(Help);
        return Failure;
    }

    private static int Fail(TextWriter errors, string message)
    {
        errors.WriteLine($"bgpwatch: {message}");
        return Failure;
    }

    /// <summary>An alert as a JSON line, in the words of the webhook payload.</summary>
    private sealed record AlertLine(string Kind, DateTime At, string Type, string Prefix, long? ObservedOrigin, IReadOnlyList<long> ExpectedOrigins,
        string Peer, IReadOnlyList<long> AsPath, string Detail)
    {
        public static AlertLine Of(Alert alert)
        {
            return new AlertLine("alert", alert.RaisedAt, alert.Type.Name(), alert.Prefix.ToString(), alert.ObservedOrigin,
                [.. alert.ExpectedOrigins.Order()], alert.Peer, alert.AsPath, alert.Detail);
        }
    }

    /// <summary>Incidents on the output: one line of text, or one JSON object, each.</summary>
    private sealed class ConsoleNotifier(bool json, TextWriter output) : INotifier
    {
        public string Name => "stdout";

        public Task NotifyAsync(Notification notification, CancellationToken cancel)
        {
            output.WriteLine(json
                ? NotificationJson.Serialize(notification)
                : $"{notification.At:HH:mm:ss} INCIDENT {notification.Kind.ToString().ToLowerInvariant()} {notification.Text}");
            return Task.CompletedTask;
        }
    }
}
