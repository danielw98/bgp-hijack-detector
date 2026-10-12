using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace BGPWatch.Web.Api;

/// <summary>
/// Per-client fixed windows on the routes that cost real work: the lab actions (Docker
/// exec in the routers), the incident reload (a database rewrite) and the lookups
/// (RIPEstat calls). The client is the connection's address, which the forwarded-headers
/// middleware sets from the proxy's X-Forwarded-For. A refused call answers 429.
/// </summary>
public static class RateLimits
{
    public const string Lab = "lab";
    public const string Replay = "replay";
    public const string Lookup = "lookup";

    public const int LabPerMinute = 30;
    public const int ReplayPerMinute = 3;
    public const int LookupPerMinute = 20;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, cancel) =>
            new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new { ok = false, error = "too many requests; try again in a minute" }, cancel));
        options.AddPolicy(Lab, http => PerClient(http, LabPerMinute));
        options.AddPolicy(Replay, http => PerClient(http, ReplayPerMinute));
        options.AddPolicy(Lookup, http => PerClient(http, LookupPerMinute));
    }

    /// <summary>The partition a request falls in: its client's address and a fixed window of permits.</summary>
    public static RateLimitPartition<string> PerClient(HttpContext http, int permitsPerMinute)
    {
        return RateLimitPartition.GetFixedWindowLimiter(ClientOf(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = Window,
            QueueLimit = 0,
        });
    }

    public static string ClientOf(HttpContext http)
    {
        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
