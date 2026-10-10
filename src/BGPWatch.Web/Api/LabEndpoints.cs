using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Bmp;
using BGPWatch.Web.Lab;

namespace BGPWatch.Web.Api;

/// <summary>
/// The lab view and the demo controls over the Docker socket: hijack scenarios, reset,
/// the kernel walk, reloading the recorded incident, clearing the lab's history; a demo
/// needs no typed commands. Enabled only when LAB_CONTROL=1 and the socket is mounted.
/// The vtysh command lists mirror lab/trigger-hijack.sh and lab/stop-hijack.sh.
/// </summary>
public static class LabEndpoints
{
    private const int OutputTail = 600;
    private const string ReloadedIncident = "youtube-2008";

    private static readonly IPNetwork LabPrefix = IPNetwork.Parse("203.0.113.0/24");

    private static readonly Dictionary<string, string[]> Scenarios = new()
    {
        ["exact"] = ["203.0.113.0/24"],
        ["subprefix"] = ["203.0.113.0/25"],
        ["both"] = ["203.0.113.0/24", "203.0.113.0/25"],
    };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/lab", LabAsync);
        app.MapPost("/api/lab/trigger", TriggerAsync);
        app.MapPost("/api/lab/reset", (Settings s, DockerEngine d, CancellationToken c) => ActAsync("reset", Scenarios["both"], s, d, c));
        app.MapPost("/api/lab/trace", TraceAsync);
        app.MapPost("/api/lab/replay", ReplayAsync);
        app.MapPost("/api/history/clear", ClearAsync);
    }

    private static async Task<IResult> LabAsync(Settings settings, DockerEngine docker, BmpStation station, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        var routers = await LabView.SnapshotAsync(async (container, command) => (await docker.ExecAsync(container, command, c)).Stdout,
            station.Reporting);
        return Results.Ok(new { ok = true, collector = LabView.Station, routers });
    }

    private static async Task<IResult> TriggerAsync(HttpRequest request, Settings settings, DockerEngine docker, CancellationToken c)
    {
        var scenario = (await BodyAsync(request, c))["scenario"]?.ToString() ?? "both";
        if (!Scenarios.TryGetValue(scenario, out var prefixes))
        {
            return Failure(StatusCodes.Status400BadRequest, "unknown scenario");
        }
        return await ActAsync("trigger", prefixes, settings, docker, c);
    }

    /// <summary>
    /// Where a packet from a lab router to an address of the lab prefix would go, asked of
    /// each router's kernel along the way. No packet is sent (see KernelWalk).
    /// </summary>
    private static async Task<IResult> TraceAsync(HttpRequest request, Settings settings, DockerEngine docker, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        var body = await BodyAsync(request, c);
        var from = body["from"]?.ToString() ?? "transit";
        if (LabView.Routers.FirstOrDefault(r => r.Role == from) is not { } router)
        {
            return Failure(StatusCodes.Status400BadRequest, "unknown router");
        }
        IPAddress destination;
        try
        {
            destination = Prefixes.ParseAddress(body["to"]?.ToString() ?? "203.0.113.1");
        }
        catch (FormatException)
        {
            return Failure(StatusCodes.Status400BadRequest, "not an address");
        }
        if (!LabPrefix.Contains(destination))
        {
            return Failure(StatusCodes.Status400BadRequest, $"destination outside {LabPrefix}");
        }
        var containers = LabView.Routers.ToDictionary(r => r.Role, r => r.Container);
        var byAddress = LabView.Routers.ToDictionary(r => r.Address, r => r.Role);
        async Task<Verdict> AskAsync(string role, string to)
        {
            var result = await docker.ExecAsync(containers[role], [.. KernelWalk.RouteGet, to], c);
            return KernelWalk.Read(result.Stdout, result.Stderr);
        }
        try
        {
            var walk = await KernelWalk.FollowAsync(router.Role, destination.ToString(), byAddress, AskAsync);
            return Results.Ok(new { ok = true, from = router.Role, to = destination.ToString(), hops = walk.Hops, outcome = walk.Outcome });
        }
        catch (DockerException e)
        {
            return Failure(StatusCodes.Status502BadGateway, e.Message);
        }
    }

    /// <summary>Reloads the recorded incident from its files, replacing what was loaded.</summary>
    private static async Task<IResult> ReplayAsync(Settings settings, IncidentLoader loader, ILogger<IncidentLoader> logger, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        try
        {
            var summary = await IncidentFiles.LoadAsync(loader, settings, ReloadedIncident, force: true, c);
            return Results.Ok(new { ok = true, summary.Source, summary.Updates, summary.Alerts, summary.Skipped });
        }
        catch (Exception e) when (e is IOException or Native.MalformedUpdateException or Npgsql.NpgsqlException)
        {
            logger.LogError(e, "reloading {Incident} failed", ReloadedIncident);
            return Failure(StatusCodes.Status502BadGateway, e.Message);
        }
    }

    /// <summary>
    /// Empties the lab's history (the recorded incident stays) and drops the BMP sessions,
    /// so the routers report their tables again.
    /// </summary>
    private static async Task<IResult> ClearAsync(Settings settings, Storage.Queries queries, BmpStation station, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        var deleted = await queries.DeleteSourceAsync(LabPipeline.Source, c);
        station.DropSessions();
        return Results.Ok(new { ok = true, alerts = deleted.Alerts, routes = deleted.Routes });
    }

    private static async Task<IResult> ActAsync(string action, string[] prefixes, Settings settings, DockerEngine docker, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        var output = "";
        long exitCode = 0;
        try
        {
            foreach (var batch in VtyshBatches(action, prefixes))
            {
                var result = await docker.ExecAsync(settings.AttackerContainer, ["vtysh", .. batch.SelectMany(line => new[] { "-c", line })], c);
                output += result.Stdout + result.Stderr;
                exitCode = Math.Max(exitCode, result.ExitCode);
            }
        }
        catch (DockerException e)
        {
            return Failure(StatusCodes.Status502BadGateway, e.Message);
        }
        // vtysh returns non-zero for benign cases (removing absent config, saving to the
        // read-only mounted frr.conf), so success is "the commands ran": the live route and
        // alert tables are the ground truth. The highest exit code is still reported.
        return Results.Ok(new
        {
            ok = true,
            action,
            prefixes,
            exit_code = exitCode,
            output = output.Length > OutputTail ? output[^OutputTail..] : output,
        });
    }

    /// <summary>
    /// The vtysh command lists of an action, each run as one vtysh call. vtysh stops at
    /// the first failing command, so a reset withdraws every prefix in a call of its own:
    /// removing a prefix that is not announced fails without keeping the others announced.
    /// </summary>
    public static List<List<string>> VtyshBatches(string action, string[] prefixes)
    {
        if (action == "trigger")
        {
            return
            [
                [
                    "configure terminal",
                    .. prefixes.Select(p => $"ip route {p} Null0"),
                    "router bgp 65003",
                    "address-family ipv4 unicast",
                    .. prefixes.Select(p => $"network {p}"),
                    "end",
                ],
            ];
        }
        return
        [
            .. prefixes.Select(p => new List<string>
            {
                "configure terminal",
                "router bgp 65003",
                "address-family ipv4 unicast",
                $"no network {p}",
                "exit-address-family",
                "exit",
                $"no ip route {p} Null0",
                "end",
            }),
        ];
    }

    private static async Task<JsonObject> BodyAsync(HttpRequest request, CancellationToken c)
    {
        using var reader = new StreamReader(request.Body);
        var text = await reader.ReadToEndAsync(c);
        try
        {
            return text.Trim() == "" ? [] : JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IResult Disabled()
    {
        return Failure(StatusCodes.Status403Forbidden, "lab control disabled");
    }

    private static IResult Failure(int status, string error)
    {
        return Results.Json(new { ok = false, error }, statusCode: status);
    }
}
