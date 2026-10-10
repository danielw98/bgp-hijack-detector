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
using BGPWatch.Web.Lookup;
using BGPWatch.Web.Native;
using BGPWatch.Web.Storage;
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
builder.Services.AddSingleton(new RipeStat(RipeStat.Over(new HttpClient()), () => Stopwatch.GetElapsedTime(started)));
builder.Services.AddRazorPages();
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

var app = builder.Build();
await RouteLog.EnsureSchemaAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), settings.Schema, CancellationToken.None);
app.UseStaticFiles();
app.MapRazorPages();
ReadEndpoints.Map(app);
LabEndpoints.Map(app);
await app.RunAsync();
return 0;
