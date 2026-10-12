namespace BGPWatch.Web.Api;

/// <summary>
/// The headers every response carries: no MIME sniffing, the origin only as referrer,
/// and a content security policy of this origin alone. The page loads no third-party
/// resource; its style attributes (lane positions) need 'unsafe-inline' for styles, the
/// favicon is a data: URI, and no other page may frame it.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static Task Middleware(HttpContext context, RequestDelegate next)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        return next(context);
    }
}
