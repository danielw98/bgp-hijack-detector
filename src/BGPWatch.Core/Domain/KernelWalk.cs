using System.Text.RegularExpressions;

namespace BGPWatch.Core.Domain;

/// <summary>A kernel's answer to <c>ip route get</c>.</summary>
public sealed record Verdict(string Kind, string? NextHop, string? Dev)
{
    public const string Via = "via";
}

/// <summary>One router asked along the walk, with its verdict.</summary>
public sealed record Hop(string Router, string Kind, string? NextHop, string? Dev);

public sealed record Walk(IReadOnlyList<Hop> Hops, string Outcome);

/// <summary>
/// Where a real packet would go in the lab, asked of each router's kernel with
/// <c>ip route get</c>: the forwarding verdict of one router, and the walk from router to
/// router that follows the verdicts until the packet is delivered or dropped.
/// No packet is sent: Docker drops any bridged packet addressed outside an internal
/// network's subnet, while the kernel's own forwarding decision is exactly what a packet
/// would follow.
/// </summary>
public static partial class KernelWalk
{
    public static readonly string[] RouteGet = ["ip", "route", "get"];
    public const int MaxHops = 6;

    private static readonly Dictionary<string, string> Outcomes = new()
    {
        ["local"] = "delivered",
        ["direct"] = "delivered",
        ["blackhole"] = "dropped",
        ["unreachable"] = "unreachable",
        ["unknown"] = "unknown",
    };

    /// <summary>
    /// The verdict: forwarded via a neighbor, delivered locally, sent directly on a link,
    /// dropped by a blackhole route, or unreachable.
    /// </summary>
    public static Verdict Read(string stdout, string stderr)
    {
        var text = stdout.Trim();
        if (ViaPattern().Match(text) is { Success: true } via)
        {
            return new Verdict(Verdict.Via, via.Groups[1].Value, via.Groups[2].Value);
        }
        if (DirectPattern().Match(text) is { Success: true } direct)
        {
            return new Verdict(text.StartsWith("local ") ? "local" : "direct", null, direct.Groups[1].Value);
        }
        if (stderr.Contains("Invalid argument"))
        {
            return new Verdict("blackhole", null, null);
        }
        if (stderr.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
        {
            return new Verdict("unreachable", null, null);
        }
        return new Verdict("unknown", null, null);
    }

    /// <summary>
    /// Follows the verdicts from router <paramref name="start"/> toward the destination.
    /// <paramref name="routers"/> maps each router's link address to its name;
    /// <paramref name="ask"/> returns a router's verdict. The outcome is delivered, dropped,
    /// unreachable, lost (the next hop is no lab router), unknown, or loop.
    /// </summary>
    public static async Task<Walk> FollowAsync(string start, string destination, IReadOnlyDictionary<string, string> routers,
        Func<string, string, Task<Verdict>> ask)
    {
        var hops = new List<Hop>();
        var router = start;
        for (var i = 0; i < MaxHops; i++)
        {
            var answer = await ask(router, destination);
            hops.Add(new Hop(router, answer.Kind, answer.NextHop, answer.Dev));
            if (answer.Kind != Verdict.Via)
            {
                return new Walk(hops, Outcomes[answer.Kind]);
            }
            if (!routers.TryGetValue(answer.NextHop ?? "", out var next))
            {
                return new Walk(hops, "lost");
            }
            router = next;
        }
        return new Walk(hops, "loop");
    }

    [GeneratedRegex(@"\bvia (\S+) dev (\S+)")]
    private static partial Regex ViaPattern();

    [GeneratedRegex(@"^(?:local )?\S+ dev (\S+)")]
    private static partial Regex DirectPattern();
}
