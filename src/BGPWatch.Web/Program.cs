using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BGPWatch.Core.Application;
using BGPWatch.Web;
using BGPWatch.Web.Api;
using BGPWatch.Web.Bmp;
using BGPWatch.Web.Config;
using BGPWatch.Web.Lab;
using BGPWatch.Web.Localization;
using BGPWatch.Web.Lookup;
using BGPWatch.Web.Native;
using BGPWatch.Web.Reading;
using BGPWatch.Web.Storage;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.WebEncoders;
using Npgsql;

if (args is ["dump", var dumped])
{
    return Cli.Dump(dumped);
}
if (args is ["measure", var config, var measured])
{
    return Cli.Measure(config, measured);
}

var builder = WebApplication.CreateBuilder(args);
var settings = Settings.From(builder.Configuration);
var started = Stopwatch.GetTimestamp();

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(settings.ConnectionString) { GssEncryptionMode = GssEncryptionMode.Disable }));
builder.Services.AddSingleton<IRouteLog, RouteLog>();
builder.Services.AddSingleton<Queries>();
builder.Services.AddSingleton<IUpdateArchive, MrtArchive>();
builder.Services.AddSingleton(sp => new IncidentLoader(
    sp.GetRequiredService<IRouteLog>(), sp.GetRequiredService<IUpdateArchive>(), sp.GetRequiredService<ILogger<IncidentLoader>>()));
builder.Services.AddSingleton(TomlFiles.LoadDetector(settings.DetectorConfig));
builder.Services.AddSingleton<LabPipeline>();
builder.Services.AddSingleton<BmpStation>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BmpStation>());
builder.Services.AddHostedService<IncidentStartup>();
builder.Services.AddSingleton(new DockerEngine(DockerEngine.Client()));
builder.Services.AddSingleton(sp => LabSnapshots.Over(
    sp.GetRequiredService<DockerEngine>(), sp.GetRequiredService<BmpStation>(), () => Stopwatch.GetElapsedTime(started)));
builder.Services.AddSingleton(new RipeStat(RipeStat.Over(new HttpClient()), () => Stopwatch.GetElapsedTime(started)));
builder.Services.AddSingleton<IReadOnlyDictionary<Language, ReadingList>>(new Dictionary<Language, ReadingList>
{
    [Language.En] = ReadingList.EmbeddedEnglish(),
    [Language.Ro] = ReadingList.EmbeddedRomanian(),
});
builder.Services.AddSingleton(Translations.Embedded());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(sp =>
{
    var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
    return new Translator(sp.GetRequiredService<Translations>(), LanguageChoice.Of(http), http?.Request.PathBase.Value ?? "");
});
builder.Services.AddRazorPages();
builder.Services.AddRateLimiter(RateLimits.Configure);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // The client address comes from the last X-Forwarded-For entry, the one the proxy
    // (nginx on a private Docker network) appended; a header from anywhere else is ignored.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var network in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128" })
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

var app = builder.Build();
app.UseForwardedHeaders();
app.Use(SecurityHeaders.Middleware);
await RouteLog.EnsureSchemaAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), settings.Schema, CancellationToken.None);
if (settings.PathBase.Length > 0)
{
    app.UsePathBase(settings.PathBase);
}
app.UseStaticFiles(new StaticFileOptions
{
    // The page's modules and styles change with every release: the browser revalidates
    // them on each load (an ETag round trip); the vendored fonts never change.
    OnPrepareResponse = context =>
    {
        var fonts = context.Context.Request.Path.StartsWithSegments("/static/fonts");
        context.Context.Response.Headers.CacheControl = fonts ? "public, max-age=31536000, immutable" : "no-cache";
    },
});
app.Use(LanguageChoice.Middleware);
app.UseRouting();
app.UseRateLimiter();
app.MapRazorPages();
app.MapGet("/ghid", (HttpContext http) => Results.Redirect($"{http.Request.PathBase}/guide{http.Request.QueryString}", permanent: true));
app.MapGet("/referinte", (HttpContext http) => Results.Redirect($"{http.Request.PathBase}/references{http.Request.QueryString}", permanent: true));
ReadEndpoints.Map(app);
LabEndpoints.Map(app);
await app.RunAsync();
return 0;
