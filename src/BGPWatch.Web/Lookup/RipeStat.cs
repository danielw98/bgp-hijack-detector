using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using BGPWatch.Core.Domain;

namespace BGPWatch.Web.Lookup;

/// <summary>RIPEstat did not answer, or answered without the expected data.</summary>
public sealed class RipeStatException(string message) : Exception(message);

public sealed record Roa(string? Prefix, long? MaxLength, long? Origin, string? Validity);

public sealed record Rpki(string Status, List<Roa> Roas);

public sealed record Origin(long Asn, string Holder, Rpki Rpki);

/// <summary>What RIPEstat says about an address or prefix today.</summary>
public sealed record Lookup(string Resource, string? Prefix, bool Announced, List<Origin> Origins);

/// <summary>The visitor's public address, where it was learned, and its lookup.</summary>
public sealed record SelfLookup(string Ip, string Via, string Resource, string? Prefix, bool Announced, List<Origin> Origins)
{
    public static SelfLookup Of(string ip, string via, Lookup lookup)
    {
        return new SelfLookup(ip, via, lookup.Resource, lookup.Prefix, lookup.Announced, lookup.Origins);
    }
}

/// <summary>
/// Thin adapter over the RIPEstat Data API: which prefix covers an address, who
/// originates it, who holds that AS, and what RPKI says about the pair. Answers are
/// cached for ten minutes. The fetcher and the clock are injected so it is tested offline.
/// </summary>
public sealed class RipeStat(Func<string, CancellationToken, Task<JsonNode?>> fetch, Func<TimeSpan> clock)
{
    public const string SourceApp = "ptirc-bgpwatch";
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(600);

    private const string Base = "https://stat.ripe.net/data/{0}/data.json";

    private readonly ConcurrentDictionary<string, (TimeSpan At, JsonNode Data)> cache = new();

    /// <summary>A fetcher over HTTP that names this application.</summary>
    public static Func<string, CancellationToken, Task<JsonNode?>> Over(HttpClient http)
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd(SourceApp);
        http.Timeout = TimeSpan.FromSeconds(15);
        return async (url, cancel) => JsonNode.Parse(await http.GetStringAsync(url, cancel));
    }

    /// <summary>
    /// The announced prefix covering an address or prefix, its origin ASes with their
    /// holders, and the RPKI validity of each (prefix, origin) pair.
    /// </summary>
    public async Task<Lookup> LookupAsync(string resource, CancellationToken cancel)
    {
        resource = Normalize(resource);
        var info = await DataAsync("network-info", cancel, ("resource", resource));
        var prefix = info["prefix"]?.GetValue<string>() ?? "";
        if (prefix == "")
        {
            return new Lookup(resource, null, false, []);
        }
        var overview = await DataAsync("prefix-overview", cancel, ("resource", prefix));
        var holders = (overview["asns"]?.AsArray() ?? [])
            .ToDictionary(a => a!["asn"]!.ToString(), a => a!["holder"]?.GetValue<string>() ?? "");
        var origins = new List<Origin>();
        foreach (var asn in (info["asns"]?.AsArray() ?? []).Select(a => long.Parse(a!.ToString())))
        {
            origins.Add(new Origin(asn, holders.GetValueOrDefault(asn.ToString(), ""), await RpkiAsync(asn, prefix, cancel)));
        }
        var announced = overview["announced"]?.GetValue<bool>() ?? origins.Count > 0;
        return new Lookup(resource, prefix, announced, origins);
    }

    /// <summary>The public address RIPEstat sees this process coming from.</summary>
    public async Task<string> MyIpAsync(CancellationToken cancel)
    {
        return (await DataAsync("whats-my-ip", cancel))["ip"]!.GetValue<string>();
    }

    /// <summary>An IP address or a CIDR prefix in canonical form; anything else is refused.</summary>
    public static string Normalize(string resource)
    {
        var text = resource.Trim();
        try
        {
            return text.Contains('/') ? Prefixes.ParseLoose(text).ToString() : Prefixes.ParseAddress(text).ToString();
        }
        catch (FormatException)
        {
            throw new ArgumentException($"not an IP address or prefix: '{text}'");
        }
    }

    private async Task<Rpki> RpkiAsync(long asn, string prefix, CancellationToken cancel)
    {
        var data = await DataAsync("rpki-validation", cancel, ("resource", $"AS{asn}"), ("prefix", prefix));
        var roas = (data["validating_roas"]?.AsArray() ?? []).Select(roa => new Roa(
            roa!["prefix"]?.GetValue<string>(),
            roa["max_length"]?.GetValue<long>(),
            roa["origin"]?.ToString() is { Length: > 0 } origin ? long.Parse(origin) : null,
            roa["validity"]?.GetValue<string>()));
        return new Rpki(data["status"]?.GetValue<string>() ?? "unknown", [.. roas]);
    }

    private async Task<JsonNode> DataAsync(string call, CancellationToken cancel, params (string Name, string Value)[] parameters)
    {
        var query = string.Join("&", parameters.Append((Name: "sourceapp", Value: SourceApp))
            .Select(p => $"{p.Name}={WebUtility.UrlEncode(p.Value)}"));
        var url = $"{string.Format(Base, call)}?{query}";
        var now = clock();
        if (cache.TryGetValue(url, out var cached) && now - cached.At < Ttl)
        {
            return cached.Data;
        }
        JsonNode? document;
        try
        {
            document = await fetch(url, cancel);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new RipeStatException($"RIPEstat {call}: {e.Message}");
        }
        var status = document?["status"]?.GetValue<string>();
        if (status != "ok" || document?["data"] is not { } data)
        {
            throw new RipeStatException($"RIPEstat {call}: {status ?? "no status"}");
        }
        cache[url] = (now, data);
        return data;
    }
}
