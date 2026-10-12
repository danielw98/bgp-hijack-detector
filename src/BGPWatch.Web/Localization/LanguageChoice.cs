namespace BGPWatch.Web.Localization;

/// <summary>
/// The language of a request: "?lang=xx" chooses it and stores it in the "lang" cookie
/// for a year; otherwise the cookie; otherwise the default, English.
/// </summary>
public static class LanguageChoice
{
    public const string Query = "lang";
    public const string Cookie = "lang";

    private static readonly object ItemKey = new();

    public static async Task Middleware(HttpContext context, RequestDelegate next)
    {
        var asked = Languages.FromCode(context.Request.Query[Query]);
        if (asked is { } chosen)
        {
            context.Response.Cookies.Append(Cookie, chosen.Code(), new CookieOptions
            {
                Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
            });
        }
        context.Items[ItemKey] = asked ?? Languages.FromCode(context.Request.Cookies[Cookie]) ?? Languages.Default;
        await next(context);
    }

    public static Language Of(HttpContext? context)
    {
        return context?.Items[ItemKey] is Language language ? language : Languages.Default;
    }
}
