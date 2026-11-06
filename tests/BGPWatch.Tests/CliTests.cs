using BGPWatch.Core.Domain;
using BGPWatch.Web;
using BGPWatch.Web.Config;
using BGPWatch.Web.Native;
using BGPWatch.Web.Watch;

namespace BGPWatch.Tests;

/// <summary>The command line: its arguments, the verbs that need no network, and what each leaves on the output and in the exit code.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "bgpwatch-tests", Guid.NewGuid().ToString("n"));

    private string Config => Path.Combine(folder, "bgpwatch.toml");

    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<(int? Code, string Output, string Errors)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var code = await Cli.RunAsync(args, output, errors);
        return (code, output.ToString(), errors.ToString());
    }

    [Fact]
    public void TheArgumentsSplitIntoVerbWordsAndOptions()
    {
        var parsed = CliArgs.Parse(["prefixes", "add", "193.0.0.0/21", "3333", "--description", "RIPE NCC", "--json"]);
        Assert.Equal("prefixes", parsed.Verb);
        Assert.Equal(["add", "193.0.0.0/21", "3333"], parsed.Words);
        Assert.Equal("RIPE NCC", parsed.Options["description"]);
        Assert.True(parsed.Json);
    }

    [Fact]
    public void AnOptionWithoutAValueIsRefused()
    {
        Assert.Throws<FormatException>(() => CliArgs.Parse(["watch", "--config"]));
    }

    [Fact]
    public async Task AnUnknownVerbLeavesTheHostToRun()
    {
        Assert.Null((await RunAsync("serve")).Code);
        Assert.Null((await RunAsync()).Code);
    }

    [Fact]
    public async Task HelpPrintsTheUsageAndFails()
    {
        var (code, _, errors) = await RunAsync("help");
        Assert.Equal(WatchCli.Failure, code);
        Assert.StartsWith("usage: bgpwatch", errors);
    }

    [Fact]
    public async Task PrefixesAddWritesTheFileAndRemoveTakesTheEntryOut()
    {
        var (code, output, _) = await RunAsync("prefixes", "add", "193.0.6.139/21", "AS3333", "--description", "RIPE NCC", "--config", Config);
        Assert.Equal(0, code);
        Assert.Contains("193.0.0.0/21", output, StringComparison.Ordinal);
        Assert.Contains("AS3333  RIPE NCC", output, StringComparison.Ordinal);
        Assert.Equal("193.0.0.0/21", Assert.Single(new WatchFile(Config).Load().Monitored).Prefix);
        var (removed, said, _) = await RunAsync("prefixes", "remove", "193.0.0.0/21", "--config", Config);
        Assert.Equal(0, removed);
        Assert.StartsWith("removed 193.0.0.0/21", said);
        Assert.Empty(new WatchFile(Config).Load().Monitored);
    }

    [Fact]
    public async Task ABadAsNumberFails()
    {
        var (code, _, errors) = await RunAsync("prefixes", "add", "193.0.0.0/21", "AS-1", "--config", Config);
        Assert.Equal(WatchCli.Failure, code);
        Assert.Contains("not an AS number", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReplayRaisesWhatTheDetectorRaisesAndExitsWithTwo()
    {
        var watch = new WatchFile(Config);
        await watch.AddAsync("208.65.152.0/22", [36561], "YouTube", CancellationToken.None);
        var file = Fixture("updates.20080224.1845.gz");
        var (code, output, errors) = await RunAsync("replay", file, "--config", Config, "--json");
        Assert.Equal(WatchCli.Found, code);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var detector = new HijackDetector(watch.Load().ToDetector().Monitored, []);
        var expected = new MrtArchive().Read(file).Routes.SelectMany(detector.Inspect).Count();
        Assert.True(expected > 0);
        Assert.Equal(expected, lines.Count(l => l.StartsWith("{\"kind\":\"alert\"", StringComparison.Ordinal)));
        Assert.Equal(1, lines.Count(l => l.StartsWith("{\"kind\":\"opened\"", StringComparison.Ordinal)));
        Assert.Contains($"{expected} alerts ({expected} subprefix_hijack), 1 incidents still open", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReplayWithNothingToRaiseExitsWithZero()
    {
        await new WatchFile(Config).AddAsync("10.0.0.0/8", [64500], "", CancellationToken.None);
        var (code, output, errors) = await RunAsync("replay", Fixture("updates.20080224.1845.gz"), "--config", Config);
        Assert.Equal(0, code);
        Assert.Equal("", output);
        Assert.Contains("0 alerts (none), 0 incidents still open", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotifyTestWithoutAWebhookFails()
    {
        var (code, _, errors) = await RunAsync("notify", "test", "--config", Config);
        Assert.Equal(WatchCli.Failure, code);
        Assert.Contains("no webhook", errors, StringComparison.Ordinal);
    }
}
