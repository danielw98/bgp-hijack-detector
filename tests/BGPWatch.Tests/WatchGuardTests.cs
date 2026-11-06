using System.Net;
using BGPWatch.Web;
using BGPWatch.Web.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BGPWatch.Tests;

/// <summary>Who may edit the watch file from the page: a private client without a token, the bearer of the token otherwise.</summary>
public class WatchGuardTests
{
    private static Settings With(string token)
    {
        return new Settings("Host=db", false, "lab-attacker", "/data/incidents", "/data/config.toml", "/data/schema.sql", "", "/data/watch/bgpwatch.toml", token);
    }

    private static DefaultHttpContext From(string address, string? authorization = null, Settings? settings = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        if (authorization is not null)
        {
            http.Request.Headers.Authorization = authorization;
        }
        http.RequestServices = new ServiceCollection().AddSingleton(settings ?? With("")).BuildServiceProvider();
        return http;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("172.18.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.5")]
    public void WithoutATokenAPrivateClientMayEdit(string address)
    {
        Assert.True(WatchGuard.Allows(From(address), With("")));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2a02:2f04::1")]
    public void WithoutATokenAPublicClientMayNot(string address)
    {
        Assert.False(WatchGuard.Allows(From(address), With("")));
    }

    [Theory]
    [InlineData("127.0.0.1", null, false)]
    [InlineData("8.8.8.8", "Bearer s3cret", true)]
    [InlineData("127.0.0.1", "Bearer s3cret", true)]
    [InlineData("8.8.8.8", "Bearer wrong", false)]
    [InlineData("8.8.8.8", "Bearer s3cre", false)]
    [InlineData("8.8.8.8", "Basic s3cret", false)]
    public void WithATokenOnlyItsBearerMayEdit(string address, string? authorization, bool allowed)
    {
        Assert.Equal(allowed, WatchGuard.Allows(From(address, authorization), With("s3cret")));
    }

    [Fact]
    public async Task TheFilterRefusesAStrangerWith403AndLetsTheBearerThrough()
    {
        var settings = With("s3cret");
        var refused = await WatchGuard.Filter(new DefaultEndpointFilterInvocationContext(From("8.8.8.8", settings: settings)), _ => ValueTask.FromResult<object?>("ran"));
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(refused).StatusCode);
        var allowed = await WatchGuard.Filter(new DefaultEndpointFilterInvocationContext(From("8.8.8.8", "Bearer s3cret", settings)), _ => ValueTask.FromResult<object?>("ran"));
        Assert.Equal("ran", allowed);
    }
}
