using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Native;

namespace BGPWatch.Web;

/// <summary>
/// The verbs the author's scripts call, with no database:
/// <c>dump &lt;updates.mrt[.gz]&gt;</c> prints every route of the file as one JSON line, and on
/// stderr how many malformed records it dropped;
/// <c>measure &lt;config.toml&gt; &lt;updates.mrt[.gz]&gt;</c> prints what the detector raises
/// on the file, which peers carried the alerted routes, and how long it takes.
/// </summary>
public static class Cli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
