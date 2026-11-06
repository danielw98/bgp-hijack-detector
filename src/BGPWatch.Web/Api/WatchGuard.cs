using System.Security.Cryptography;
using System.Text;
using BGPWatch.Web.Lookup;

namespace BGPWatch.Web.Api;

/// <summary>
/// Who may change the watch file from the page: a request carrying the token in
/// "Authorization: Bearer", or, when no token is configured, a client on a private
/// network (the local demo); anyone else is refused with 403.
/// </summary>
public static class WatchGuard
{
    public const string Refused = "editing the watchlist needs the watch token";

    public static bool Allows(HttpContext http, Settings settings)
    {
        if (settings.WatchToken.Length > 0)
        {
            return Bearer(http) is { } token
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(settings.WatchToken));
        }
        var remote = http.Connection.RemoteIpAddress;
        return remote is not null && !AddressScope.IsGlobal(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);
    }

    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var settings = context.HttpContext.RequestServices.GetRequiredService<Settings>();
        return Allows(context.HttpContext, settings)
            ? await next(context)
            : Results.Json(new { ok = false, error = Refused }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static string? Bearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..].Trim() : null;
    }
}
