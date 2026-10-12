namespace BGPWatch.Web.Api;

/// <summary>
/// Refuses a POST whose body is not declared as JSON with 415. A cross-site page can send
/// a form or a text/plain body without asking, but a JSON one needs a CORS preflight,
/// which this app never grants; the page's own modules always send JSON.
/// </summary>
public static class JsonOnly
{
    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.HasJsonContentType())
        {
            return Results.Json(new { ok = false, error = "the body must be JSON (Content-Type: application/json)" }, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }
        return await next(context);
    }
}
