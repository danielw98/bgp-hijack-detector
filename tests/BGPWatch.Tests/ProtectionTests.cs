using System.Net;
using System.Text.Json.Nodes;
using BGPWatch.Web.Api;
using BGPWatch.Web.Lookup;
using Microsoft.AspNetCore.Http;

namespace BGPWatch.Tests;

/// <summary>The guards of the public instance: security headers, JSON-only lab POSTs, per-client rate limits and the bounded lookup cache.</summary>
public class ProtectionTests
{
    [Fact]
    public async Task EveryResponseCarriesTheSecurityHeaders()
    {
        var http = new DefaultHttpContext();
        await SecurityHeaders.Middleware(http, _ => Task.CompletedTask);
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("strict-origin-when-cross-origin", http.Response.Headers["Referrer-Policy"].ToString());
        Assert.Contains("default-src 'self'", http.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", http.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task APostWithoutAJsonBodyIsRefused(string? contentType)
    {
        var result = await RunJsonOnly(contentType);
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    public async Task APostWithAJsonBodyGoesThrough(string contentType)
    {
        Assert.Equal("ran", await RunJsonOnly(contentType));
    }

    [Fact]
    public void AClientGetsItsPermitsAndThenIsRefused()
    {
        var limiter = Limiter("198.51.100.7", 3);
        Assert.All(Enumerable.Range(0, 3), _ => Assert.True(limiter.AttemptAcquire().IsAcquired));
        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void ClientsArePartitionedByTheirAddress()
    {
        Assert.NotEqual(Partition("198.51.100.7"), Partition("198.51.100.8"));
        Assert.Equal(Partition("198.51.100.7"), Partition("198.51.100.7"));
    }

    [Fact]
    public async Task TheLookupCacheStaysBounded()
    {
        var ripe = new RipeStat(Answer, () => TimeSpan.Zero);
        for (var i = 0; i < RipeStat.MaxCached + 50; i++)
        {
            await ripe.LookupAsync($"10.{i / 256}.{i % 256}.1", CancellationToken.None);
        }
        Assert.InRange(ripe.CachedCount, 1, RipeStat.MaxCached);
    }

    [Fact]
    public async Task AFullCacheDropsTheExpiredAnswersFirst()
    {
        var now = TimeSpan.Zero;
        var ripe = new RipeStat(Answer, () => now);
        for (var i = 0; i < RipeStat.MaxCached; i++)
        {
            await ripe.LookupAsync($"10.{i / 256}.{i % 256}.1", CancellationToken.None);
        }
        now = RipeStat.Ttl * 2;
        await ripe.LookupAsync("192.0.2.1", CancellationToken.None);
        Assert.InRange(ripe.CachedCount, 1, 3);
    }

    private static Task<JsonNode?> Answer(string url, CancellationToken cancel)
    {
        var call = new Uri(url).AbsolutePath.Split('/')[2];
        var data = call switch
        {
            "network-info" => """{"asns": ["64500"], "prefix": "192.0.2.0/24"}""",
            "prefix-overview" => """{"announced": true, "asns": [{"asn": 64500, "holder": "TEST"}]}""",
            _ => """{"status": "valid", "validating_roas": []}""",
        };
        return Task.FromResult<JsonNode?>(new JsonObject { ["status"] = "ok", ["data"] = JsonNode.Parse(data) });
    }

    private static async Task<object?> RunJsonOnly(string? contentType)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.ContentType = contentType;
        return await JsonOnly.Filter(new DefaultEndpointFilterInvocationContext(http), _ => ValueTask.FromResult<object?>("ran"));
    }

    private static string Partition(string address)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return RateLimits.PerClient(http, 1).PartitionKey;
    }

    private static System.Threading.RateLimiting.RateLimiter Limiter(string address, int permits)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        var partition = RateLimits.PerClient(http, permits);
        return partition.Factory(partition.PartitionKey);
    }
}
