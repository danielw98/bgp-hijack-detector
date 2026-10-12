using System.Net;
using System.Text.Json.Nodes;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Localization;
using BGPWatch.Web.Lookup;
using BGPWatch.Web.Storage;

namespace BGPWatch.Web.Api;

/// <summary>
/// The API behind the page: incidents grouped from the alerts, the history of a prefix,
/// the recorded incident with its timeline, and a RIPEstat lookup.
/// </summary>
public static class ReadEndpoints
{
    private const int MaxLimit = 500;
    private const int IncidentAlerts = 5000;
    private const int HistoryRoutes = 2000;
    private const int HistoryChanges = 500;
    private const int HistoryAlerts = 500;
    private const int MaxResource = 64;

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/stats", (Queries q, CancellationToken c) => q.StatsAsync(c));
        app.MapGet("/api/alerts", async (Queries q, CancellationToken c, int limit = 100) =>
            limit > MaxLimit ? TooLarge("limit", MaxLimit) : Results.Ok(await q.AlertsAsync(limit, c)));
        app.MapGet("/api/routes", async (Queries q, CancellationToken c, int limit = 100) =>
            limit > MaxLimit ? TooLarge("limit", MaxLimit) : Results.Ok(await q.RoutesAsync(limit, c)));
        app.MapGet("/api/monitored", (Queries q, CancellationToken c) => q.MonitoredAsync(c));
        app.MapGet("/api/incidents", IncidentsAsync);
        app.MapGet("/api/incidents/{name}", Incident);
        app.MapGet("/api/incidents/{name}/updates", IncidentUpdatesAsync);
        app.MapGet("/api/prefix/{**prefix}", PrefixHistoryAsync);
        app.MapGet("/api/lookup", LookupAsync).RequireRateLimiting(RateLimits.Lookup);
        app.MapGet("/api/lookup/self", LookupSelfAsync).RequireRateLimiting(RateLimits.Lookup);
    }

    /// <summary>Alerts grouped into incidents, newest first.</summary>
    private static async Task<List<Incident>> IncidentsAsync(Queries q, CancellationToken c)
    {
        var alerts = await q.AlertsAsync(IncidentAlerts, c);
        var monitored = await q.MonitoredAsync(c);
        return Incidents.Group(alerts, monitored.Select(m => m.Prefix));
    }

    /// <summary>The recorded incident's ground truth, documented timeline and AS names.</summary>
    private static IResult Incident(string name, Settings settings, HttpContext http)
    {
        if (settings.IncidentDir(name) is not { } folder)
        {
            return Detail(StatusCodes.Status404NotFound, "unknown incident");
        }
        var file = TomlFiles.LoadIncident(Path.Combine(folder, "config.toml"));
        var namesPath = Path.Combine(folder, "as-names.json");
        var names = File.Exists(namesPath) ? JsonNode.Parse(File.ReadAllText(namesPath)) : null;
        return Results.Ok(new
        {
            name,
            monitored = file.Monitored,
            events = file.Events.Select(e => new { e.At, Label = e.LabelIn(LanguageChoice.Of(http)) }),
            as_names = names?["names"] ?? new JsonObject(),
            as_names_fetched = names?["fetched"],
        });
    }

    /// <summary>Every update of the incident that falls inside its monitored prefixes, in time order.</summary>
    private static async Task<IResult> IncidentUpdatesAsync(string name, Settings settings, Queries q, CancellationToken c)
    {
        if (settings.IncidentDir(name) is not { } folder)
        {
            return Detail(StatusCodes.Status404NotFound, "unknown incident");
        }
        var prefixes = TomlFiles.LoadIncident(Path.Combine(folder, "config.toml")).Monitored.Select(m => m.Prefix).ToArray();
        return Results.Ok(await q.SourceRoutesWithinAsync(name, prefixes, c));
    }

    /// <summary>The state of a prefix at every vantage point, its changes, and its alerts.</summary>
    private static async Task<IResult> PrefixHistoryAsync(string prefix, Queries q, CancellationToken c)
    {
        string network;
        try
        {
            network = Prefixes.ParseLoose(prefix).ToString();
        }
        catch (FormatException)
        {
            return Detail(StatusCodes.Status400BadRequest, "not a prefix");
        }
        var rows = await q.RoutesWithinAsync(network, HistoryRoutes, c);
        return Results.Ok(new
        {
            prefix = network,
            monitored = await q.MonitoredAsync(network, c),
            current = PrefixHistory.Current(rows),
            changes = PrefixHistory.Changes(rows).Take(HistoryChanges),
            alerts = await q.AlertsWithinAsync(network, HistoryAlerts, c),
            truncated = rows.Count == HistoryRoutes,
        });
    }

    /// <summary>What the Internet says today about an address or prefix, via RIPEstat.</summary>
    private static async Task<IResult> LookupAsync(RipeStat ripe, ILoggerFactory logs, CancellationToken c, string? resource = null)
    {
        if (resource is null || resource.Length > MaxResource)
        {
            return TooLarge("resource", MaxResource);
        }
        try
        {
            return Results.Ok(await ripe.LookupAsync(resource, c));
        }
        catch (ArgumentException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (RipeStatException e)
        {
            return RipeStatSilent(logs, e);
        }
    }

    /// <summary>
    /// The visitor's public address and its lookup: the browser's address when it is
    /// public, else the address RIPEstat sees this server coming from.
    /// </summary>
    private static async Task<IResult> LookupSelfAsync(HttpContext context, RipeStat ripe, ILoggerFactory logs, CancellationToken c)
    {
        var remote = context.Connection.RemoteIpAddress;
        var client = (remote is { IsIPv4MappedToIPv6: true } ? remote.MapToIPv4() : remote)?.ToString() ?? "";
        try
        {
            var (address, via) = IPAddress.TryParse(client, out var ip) && AddressScope.IsGlobal(ip)
                ? (client, "browser")
                : (await ripe.MyIpAsync(c), "server");
            return Results.Ok(SelfLookup.Of(address, via, await ripe.LookupAsync(address, c)));
        }
        catch (Exception e) when (e is ArgumentException or RipeStatException)
        {
            return RipeStatSilent(logs, e);
        }
    }

    /// <summary>A failed RIPEstat call: logged in full, answered with a fixed sentence.</summary>
    private static IResult RipeStatSilent(ILoggerFactory logs, Exception e)
    {
        logs.CreateLogger("BGPWatch.Lookup").LogWarning(e, "RIPEstat lookup failed");
        return Results.Json(new { error = "RIPEstat did not answer; try again later" }, statusCode: StatusCodes.Status502BadGateway);
    }

    public static IResult Detail(int status, string detail)
    {
        return Results.Json(new { detail }, statusCode: status);
    }

    private static IResult TooLarge(string field, int max)
    {
        return Detail(StatusCodes.Status422UnprocessableEntity, $"{field} is required and at most {max}");
    }
}
