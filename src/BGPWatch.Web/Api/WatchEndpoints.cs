using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using BGPWatch.Web.Config;
using BGPWatch.Web.Lookup;
using BGPWatch.Web.Storage;
using BGPWatch.Web.Watch;

namespace BGPWatch.Web.Api;

/// <summary>
/// The Internet monitor behind the page: its state (the feed, the watched prefixes with
/// the origins seen now, the open incidents), its route log, and the edits of the watch
/// file, behind WatchGuard: a prefix added or replaced, removed, every prefix of an AS
/// added, and a test notification. The page adds prefixes of at least /8 (IPv4) or /16
/// (IPv6), at most MaxPerAs from one AS and MaxWatched in all; the command line has no limit.
/// </summary>
public static class WatchEndpoints
{
    public const int MaxWatched = 100;
    public const int MaxPerAs = 50;
    public const int MinIpv4Length = 8;
    public const int MinIpv6Length = 16;
    private const int MaxLimit = 500;
    private const int EntryEvents = 40;
    private const int MaxDescription = 120;
    private const string RipeStatSilent = "RIPEstat did not answer; try again later";

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/watch", State);
        app.MapGet("/api/watch/events", async (Queries q, CancellationToken c, long? since = null, int limit = EntryEvents) =>
            limit is < 1 or > MaxLimit ? ReadEndpoints.Detail(StatusCodes.Status422UnprocessableEntity, $"limit is required and at most {MaxLimit}")
                : Results.Ok(await q.SourceRoutesAfterAsync(WatchService.Source, since, limit, c)));
        app.MapPost("/api/watch/prefixes", AddAsync).AddEndpointFilter(JsonOnly.Filter).AddEndpointFilter(WatchGuard.Filter).RequireRateLimiting(RateLimits.Lab);
        app.MapDelete("/api/watch/prefixes/{**prefix}", RemoveAsync).AddEndpointFilter(WatchGuard.Filter).RequireRateLimiting(RateLimits.Lab);
        app.MapPost("/api/watch/asn", AddAsAsync).AddEndpointFilter(JsonOnly.Filter).AddEndpointFilter(WatchGuard.Filter).RequireRateLimiting(RateLimits.Lookup);
        app.MapPost("/api/watch/test", TestAsync).AddEndpointFilter(JsonOnly.Filter).AddEndpointFilter(WatchGuard.Filter).RequireRateLimiting(RateLimits.Lab);
    }

    /// <summary>Why the page may not add a prefix this wide, or null when it may.</summary>
    public static string? TooWide(IPNetwork network)
    {
        var shortest = network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? MinIpv4Length : MinIpv6Length;
        return network.PrefixLength < shortest
            ? $"a prefix shorter than /{MinIpv4Length} (IPv4) or /{MinIpv6Length} (IPv6) streams too much of the Internet; add it from the command line"
            : null;
    }

    /// <summary>The monitor now: the feed, the ROAs, the watched prefixes with what is seen for each, the open incidents.</summary>
    private static IResult State(HttpContext http, Settings settings, WatchService service, IncidentNotifier desk)
    {
        var status = service.Status;
        var open = desk.Open(WatchService.Source);
        return Results.Ok(new
        {
            ok = true,
            source = WatchService.Source,
            editable = WatchGuard.Allows(http, settings),
            token_required = settings.WatchToken.Length > 0,
            feed = status.Feed,
            rpki = new { vrps = status.Vrps, refreshed_at = status.VrpsRefreshedAt },
            seeded_at = status.SeededAt,
            seeding = status.Seeding,
            webhooks = status.Settings.Webhook.Select(w => w.Name),
            collectors = status.Settings.Ris.Hosts,
            prefixes = WatchView.Of(status.Settings.Monitored, desk.Routes(WatchService.Source), open, status.SeedFailures),
            incidents = open,
        });
    }

    /// <summary>Adds a prefix with its expected origins, or replaces its entry; the monitor applies the file at once.</summary>
    private static async Task<IResult> AddAsync(HttpRequest request, WatchFile file, WatchService service, CancellationToken c)
    {
        var body = await JsonBody.ReadAsync(request, c);
        IPNetwork network;
        try
        {
            network = Prefixes.ParseLoose(body["prefix"]?.ToString() ?? "");
        }
        catch (FormatException)
        {
            return Failure(StatusCodes.Status400BadRequest, "not a prefix");
        }
        if (TooWide(network) is { } wide)
        {
            return Failure(StatusCodes.Status400BadRequest, wide);
        }
        if (JsonBody.Numbers(body, "expected_origins") is not { } origins)
        {
            return Failure(StatusCodes.Status400BadRequest, "expected_origins must be a list of AS numbers");
        }
        var description = body["description"]?.ToString() ?? "";
        if (description.Length > MaxDescription)
        {
            return Failure(StatusCodes.Status400BadRequest, $"description is at most {MaxDescription} characters");
        }
        return await EditingAsync(async () =>
        {
            var current = file.Load();
            var prefix = network.ToString();
            if (current.Monitored.Count >= MaxWatched && current.Monitored.All(m => m.Prefix != prefix))
            {
                return Failure(StatusCodes.Status409Conflict, $"the page watches at most {MaxWatched} prefixes; add more from the command line");
            }
            var settings = await file.AddAsync(prefix, origins, description, CancellationToken.None);
            await service.ApplyAsync(settings, CancellationToken.None);
            return Results.Ok(new { ok = true, prefix, monitored = settings.Monitored });
        });
    }

    private static async Task<IResult> RemoveAsync(string prefix, WatchFile file, WatchService service)
    {
        string network;
        try
        {
            network = Prefixes.ParseLoose(prefix).ToString();
        }
        catch (FormatException)
        {
            return Failure(StatusCodes.Status400BadRequest, "not a prefix");
        }
        return await EditingAsync(async () =>
        {
            var before = file.Load().Monitored.Count;
            var settings = await file.RemoveAsync(network, CancellationToken.None);
            await service.ApplyAsync(settings, CancellationToken.None);
            return Results.Ok(new { ok = true, removed = settings.Monitored.Count < before, monitored = settings.Monitored });
        });
    }

    /// <summary>Adds every prefix RIPEstat sees the AS announcing today, with that AS as the expected origin.</summary>
    private static async Task<IResult> AddAsAsync(HttpRequest request, WatchFile file, WatchService service, RipeStat ripe, ILoggerFactory logs, CancellationToken c)
    {
        var body = await JsonBody.ReadAsync(request, c);
        if (body["asn"] is not JsonValue value || !value.TryGetValue<long>(out var asn) || asn < 0)
        {
            return Failure(StatusCodes.Status400BadRequest, "asn must be an AS number");
        }
        var description = body["description"]?.ToString() ?? $"AS{asn}";
        if (description.Length > MaxDescription)
        {
            return Failure(StatusCodes.Status400BadRequest, $"description is at most {MaxDescription} characters");
        }
        List<string> announced;
        try
        {
            announced = await ripe.AnnouncedPrefixesAsync(asn, c);
        }
        catch (RipeStatException e)
        {
            logs.CreateLogger("BGPWatch.Watch").LogWarning(e, "announced prefixes of AS{Asn} not read", asn);
            return Failure(StatusCodes.Status502BadGateway, RipeStatSilent);
        }
        return await EditingAsync(async () =>
        {
            var known = file.Load().Monitored.Select(m => m.Prefix).ToHashSet(StringComparer.Ordinal);
            var fresh = announced.Where(p => !known.Contains(p) && TooWide(IPNetwork.Parse(p)) is null).Distinct().ToList();
            if (fresh.Count > MaxPerAs)
            {
                return Failure(StatusCodes.Status409Conflict, $"AS{asn} announces {fresh.Count} prefixes not yet watched; the page adds at most {MaxPerAs} at once");
            }
            if (known.Count + fresh.Count > MaxWatched)
            {
                return Failure(StatusCodes.Status409Conflict, $"the page watches at most {MaxWatched} prefixes; add more from the command line");
            }
            var settings = await file.EditAsync(
                current => current with { Monitored = [.. current.Monitored, .. fresh.Select(p => new MonitoredEntry(p, [asn], description))] },
                CancellationToken.None);
            await service.ApplyAsync(settings, CancellationToken.None);
            return Results.Ok(new { ok = true, asn, announced, added = fresh.Count, monitored = settings.Monitored });
        });
    }

    private static async Task<IResult> TestAsync(IncidentNotifier desk, CancellationToken c)
    {
        var failed = await desk.TestAsync(DateTime.UtcNow, c);
        var names = desk.Notifiers.Select(n => n.Name).ToList();
        return Results.Ok(new { ok = failed.Count == 0, webhooks = names, failed });
    }

    /// <summary>An edit of the watch file; a file that cannot be read or written answers 500 with the reason.</summary>
    private static async Task<IResult> EditingAsync(Func<Task<IResult>> edit)
    {
        try
        {
            return await edit();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Tomlyn.TomlException)
        {
            return Failure(StatusCodes.Status500InternalServerError, $"the watch file cannot be edited: {e.Message}");
        }
    }

    private static IResult Failure(int status, string error)
    {
        return Results.Json(new { ok = false, error }, statusCode: status);
    }
}
