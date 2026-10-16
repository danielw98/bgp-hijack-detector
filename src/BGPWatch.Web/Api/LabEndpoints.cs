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
/// the victim's fresh announcement, origin validation at the transit, the kernel walk,
/// reloading the recorded incident, clearing the lab's history; a demo
/// needs no typed commands. Enabled only when LAB_CONTROL=1 and the socket is mounted.
/// The vtysh command lists mirror lab/trigger-hijack.sh and lab/stop-hijack.sh.
/// </summary>
public static class LabEndpoints
{
    private const int OutputTail = 600;
    private const string LogCategory = "BGPWatch.Lab";
    private const string RoutersSilent = "the lab routers did not answer";
    private const string ReloadFailed = "the incident could not be reloaded";
    private const string ReloadedIncident = "youtube-2008";

    private static readonly IPNetwork LabPrefix = IPNetwork.Parse("203.0.113.0/24");
    private static readonly TimeSpan AnnouncePause = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<string, string[]> Scenarios = new()
    {
        ["exact"] = ["203.0.113.0/24"],
        ["subprefix"] = ["203.0.113.0/25"],
        ["both"] = ["203.0.113.0/24", "203.0.113.0/25"],
    };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/lab", LabAsync);
        app.MapPost("/api/lab/trigger", TriggerAsync).AddEndpointFilter(JsonOnly.Filter).RequireRateLimiting(RateLimits.Lab);
        app.MapPost("/api/lab/reset", (Settings s, DockerEngine d, LabSnapshots l, ILoggerFactory g, CancellationToken c) => ActAsync("reset", Scenarios["both"], s, d, l, g, c))
            .AddEndpointFilter(JsonOnly.Filter).RequireRateLimiting(RateLimits.Lab);
        app.MapPost("/api/lab/announce", AnnounceAsync).AddEndpointFilter(JsonOnly.Filter).RequireRateLimiting(RateLimits.Lab);
        app.MapPost("/api/lab/rov", RovAsync).AddEndpointFilter(JsonOnly.Filter).RequireRateLimiting(RateLimits.Lab);
        app.MapPost("/api/lab/trace", TraceAsync).AddEndpointFilter(JsonOnly.Filter).RequireRateLimiting(RateLimits.Lab);
        app.MapPost("/api/lab/replay", ReplayAsync).AddEndpointFilter(JsonOnly.Filter).RequireRateLimiting(RateLimits.Replay);
        app.MapPost("/api/history/clear", ClearAsync).AddEndpointFilter(JsonOnly.Filter).RequireRateLimiting(RateLimits.Lab);
    }

    private static async Task<IResult> LabAsync(Settings settings, LabSnapshots snapshots, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        var routers = await snapshots.CurrentAsync(c);
        return Results.Ok(new { ok = true, collector = LabView.Station, routers });
    }

    private static async Task<IResult> TriggerAsync(HttpRequest request, Settings settings, DockerEngine docker, LabSnapshots snapshots, ILoggerFactory logs, CancellationToken c)
    {
        var scenario = (await BodyAsync(request, c))["scenario"]?.ToString() ?? "both";
        if (!Scenarios.TryGetValue(scenario, out var prefixes))
        {
            return Failure(StatusCodes.Status400BadRequest, "unknown scenario");
        }
        return await ActAsync("trigger", prefixes, settings, docker, snapshots, logs, c);
    }

    /// <summary>
    /// The victim withdraws its prefix and announces it again a second later, so the
    /// announcement travels the lab afresh.
    /// </summary>
    private static async Task<IResult> AnnounceAsync(Settings settings, DockerEngine docker, LabSnapshots snapshots, ILoggerFactory logs, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        try
        {
            var (withdraw, announce) = AnnounceBatches();
            await RunBatchesAsync(docker, ContainerOf("victim"), [withdraw], c);
            snapshots.Expire();
            await Task.Delay(AnnouncePause, c);
            await RunBatchesAsync(docker, ContainerOf("victim"), [announce], c);
        }
        catch (DockerException e)
        {
            logs.CreateLogger(LogCategory).LogError(e, "lab action announce failed");
            return Failure(StatusCodes.Status502BadGateway, RoutersSilent);
        }
        finally
        {
            snapshots.Expire();
        }
        return Results.Ok(new { ok = true });
    }

    /// <summary>Applies route-map ROV to the attacker's session at the transit, or removes it.</summary>
    private static async Task<IResult> RovAsync(HttpRequest request, Settings settings, DockerEngine docker, LabSnapshots snapshots, ILoggerFactory logs, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        if ((await BodyAsync(request, c))["on"] is not JsonValue value || !value.TryGetValue<bool>(out var on))
        {
            return Failure(StatusCodes.Status400BadRequest, "on must be true or false");
        }
        try
        {
            await RunBatchesAsync(docker, ContainerOf("transit"), RovBatches(on), c);
        }
        catch (DockerException e)
        {
            logs.CreateLogger(LogCategory).LogError(e, "lab action rov {On} failed", on);
            return Failure(StatusCodes.Status502BadGateway, RoutersSilent);
        }
        finally
        {
            snapshots.Expire();
        }
        return Results.Ok(new { ok = true, rov = on });
    }

    /// <summary>
    /// Where a packet from a lab router to an address of the lab prefix would go, asked of
    /// each router's kernel along the way. No packet is sent (see KernelWalk).
    /// </summary>
    private static async Task<IResult> TraceAsync(HttpRequest request, Settings settings, DockerEngine docker, ILoggerFactory logs, CancellationToken c)
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
            logs.CreateLogger(LogCategory).LogError(e, "kernel walk from {Router} to {Destination} failed", router.Role, destination);
            return Failure(StatusCodes.Status502BadGateway, RoutersSilent);
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
        catch (Exception e) when (e is IOException or Npgsql.NpgsqlException)
        {
            logger.LogError(e, "reloading {Incident} failed", ReloadedIncident);
            return Failure(StatusCodes.Status502BadGateway, ReloadFailed);
        }
    }

    /// <summary>
    /// Empties the lab's history (the recorded incident stays) and drops the BMP sessions,
    /// so the routers report their tables again.
    /// </summary>
    private static async Task<IResult> ClearAsync(Settings settings, Storage.Queries queries, BmpStation station, LabSnapshots snapshots, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        var deleted = await queries.DeleteSourceAsync(LabPipeline.Source, c);
        station.DropSessions();
        snapshots.Expire();
        return Results.Ok(new { ok = true, alerts = deleted.Alerts, routes = deleted.Routes });
    }

    private static async Task<IResult> ActAsync(string action, string[] prefixes, Settings settings, DockerEngine docker, LabSnapshots snapshots,
        ILoggerFactory logs, CancellationToken c)
    {
        if (!settings.LabControl)
        {
            return Disabled();
        }
        string output;
        long exitCode;
        try
        {
            (output, exitCode) = await RunBatchesAsync(docker, settings.AttackerContainer, VtyshBatches(action, prefixes), c);
        }
        catch (DockerException e)
        {
            logs.CreateLogger(LogCategory).LogError(e, "lab action {Action} failed", action);
            return Failure(StatusCodes.Status502BadGateway, RoutersSilent);
        }
        finally
        {
            snapshots.Expire();
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

    /// <summary>
    /// The victim's fresh announcement: one vtysh call withdraws the prefix, the next one
    /// announces it again.
    /// </summary>
    public static (List<string> Withdraw, List<string> Announce) AnnounceBatches()
    {
        List<string> Victim(string network) => ["configure terminal", "router bgp 65001", "address-family ipv4 unicast", network, "end"];
        return (Victim($"no network {LabPrefix}"), Victim($"network {LabPrefix}"));
    }

    /// <summary>
    /// Origin validation on the attacker's session at the transit, on or off, then a soft
    /// inbound refresh of that session so the routes it already sent pass the new policy.
    /// The refresh is a call of its own: it runs even when removing an absent route-map fails.
    /// </summary>
    public static List<List<string>> RovBatches(bool on)
    {
        var line = $"neighbor 10.10.0.13 route-map {LabView.RovRouteMap} in";
        return
        [
            ["configure terminal", "router bgp 65000", "address-family ipv4 unicast", on ? line : $"no {line}", "end"],
            ["clear ip bgp 10.10.0.13 soft in"],
        ];
    }

    /// <summary>Runs each batch as one vtysh call in the container; returns the joined output and the highest exit code.</summary>
    private static async Task<(string Output, long ExitCode)> RunBatchesAsync(DockerEngine docker, string container, IEnumerable<List<string>> batches,
        CancellationToken c)
    {
        var output = "";
        long exitCode = 0;
        foreach (var batch in batches)
        {
            var result = await docker.ExecAsync(container, ["vtysh", .. batch.SelectMany(line => new[] { "-c", line })], c);
            output += result.Stdout + result.Stderr;
            exitCode = Math.Max(exitCode, result.ExitCode);
        }
        return (output, exitCode);
    }

    private static string ContainerOf(string role)
    {
        return LabView.Routers.Single(r => r.Role == role).Container;
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
