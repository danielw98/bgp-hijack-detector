using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Native;
using BGPWatch.Web.Watch;

namespace BGPWatch.Web;

/// <summary>
/// The command line: <c>serve</c> leaves the web host to run; every other command runs
/// here, with no database, and exits. The manual (<see cref="Manual"/>) lists them: the
/// operator's are WatchCli's, the author's are <c>dump</c> and <c>measure</c>.
/// </summary>
public static class Cli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The exit code of the command the arguments name, or null for <c>serve</c>, when the web host runs.</summary>
    public static async Task<int?> RunAsync(string[] args, TextWriter output, TextWriter errors)
    {
        switch (args)
        {
            case ["serve"]:
                return null;
            case []:
                errors.Write(Manual.Usage(""));
                return WatchCli.Failure;
            case ["help" or "--help" or "-h"]:
                output.Write(Manual.Page());
                return 0;
            case ["help", var name]:
                if (Manual.Section(name) is { } section)
                {
                    output.Write(section);
                    return 0;
                }
                return Unknown(name, errors);
            case ["--version"]:
                output.WriteLine($"{Manual.Name} {Manual.Version}");
                return 0;
            case ["dump", var path]:
                return Dump(path);
            case ["measure", var config, var path]:
                return Measure(config, path);
            case [var verb, ..] when WatchCli.Verbs.Contains(verb):
                return await WatchCli.RunAsync(args, output, errors);
            case [var verb, ..] when Manual.Commands.Any(c => c.Name == verb):
                errors.Write(Manual.Usage(verb));
                return WatchCli.Failure;
            default:
                return Unknown(args[0], errors);
        }
    }

    private static int Unknown(string name, TextWriter errors)
    {
        errors.WriteLine($"{Manual.Name}: unknown command '{name}'. Try '{Manual.Name} help'.");
        return WatchCli.Failure;
    }

    public static int Dump(string path)
    {
        var file = new MrtArchive().Read(path);
        using (var output = new StreamWriter(Console.OpenStandardOutput()))
        {
            foreach (var route in file.Routes)
            {
                output.WriteLine(JsonSerializer.Serialize(new DumpedRoute(route.SeenAt, route.Peer, route.Prefix.ToString(),
                    route.Withdrawn, route.AsPath, route.NextHop), Json));
            }
        }
        if (file.MalformedRecords > 0)
        {
            Console.Error.WriteLine($"malformed records dropped: {file.MalformedRecords}");
        }
        return 0;
    }

    public static int Measure(string configPath, string path)
    {
        var config = TomlFiles.LoadDetector(configPath);
        var detector = new HijackDetector(config.Monitored, config.Vrps);
        var clock = Stopwatch.StartNew();
        var file = new MrtArchive().Read(path);
        var routes = file.Routes;
        var parsed = clock.Elapsed;
        var alerts = routes.SelectMany(detector.Inspect).ToList();
        var detected = clock.Elapsed;
        var firstByPeer = new Dictionary<string, DateTime>();
        foreach (var alert in alerts)
        {
            firstByPeer.TryAdd(alert.Peer, alert.RaisedAt);
        }
        var measurement = new Measurement(
            routes.Count,
            routes.Count(r => r.Withdrawn),
            file.MalformedRecords,
            routes.Select(r => r.Peer).Distinct().Count(),
            alerts.Count,
            alerts.GroupBy(a => a.Type.Name()).ToDictionary(g => g.Key, g => g.Count()),
            [.. alerts.Select(a => a.Prefix.ToString()).Distinct().Order(StringComparer.Ordinal)],
            [.. alerts.Select(a => a.ObservedOrigin).Distinct().Order()],
            alerts.Count > 0 ? alerts[0].RaisedAt : null,
            alerts.Count > 0 ? alerts[^1].RaisedAt : null,
            firstByPeer,
            Math.Round(parsed.TotalSeconds, 4),
            Math.Round((detected - parsed).TotalSeconds, 4));
        Console.WriteLine(JsonSerializer.Serialize(measurement, new JsonSerializerOptions(Json) { WriteIndented = true }));
        return 0;
    }

    private sealed record DumpedRoute(DateTime SeenAt, string Peer, string Prefix, bool Withdrawn, IReadOnlyList<long> AsPath, string? NextHop);

    private sealed record Measurement(
        int Updates,
        int Withdrawals,
        int MalformedRecords,
        int PeersInFile,
        int Alerts,
        Dictionary<string, int> AlertsByType,
        List<string> AlertedPrefixes,
        List<long?> AlertedOrigins,
        DateTime? FirstAlert,
        DateTime? LastAlert,
        Dictionary<string, DateTime> FirstAlertByPeer,
        double ParseSeconds,
        double DetectSeconds);
}
