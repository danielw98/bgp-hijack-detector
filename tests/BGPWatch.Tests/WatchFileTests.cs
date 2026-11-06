using BGPWatch.Web.Config;

namespace BGPWatch.Tests;

/// <summary>The watch file: parsed, written whole, edited in place, and read back.</summary>
public sealed class WatchFileTests : IDisposable
{
    private const string Example = """
        [[monitored]]
        prefix = "193.0.0.0/21"
        expected_origins = [3333]
        description = "RIPE NCC"

        [[monitored]]
        prefix = "2001:db8::/32"
        expected_origins = [64496, 64497]

        [[vrp]]
        prefix = "193.0.0.0/21"
        max_length = 21
        origin_as = 3333

        [ris]
        hosts = ["rrc00", "rrc21"]

        [rpki]
        refresh_minutes = 30

        [[webhook]]
        name = "ops"
        url = "https://example.org/hooks/bgp"

        [history]
        retention_days = 7
        """;

    private readonly string folder = Path.Combine(Path.GetTempPath(), "bgpwatch-tests", Guid.NewGuid().ToString("n"));

    private WatchFile Watch => new(Path.Combine(folder, "bgpwatch.toml"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AMissingFileHoldsNothingAndTheDefaults()
    {
        var settings = Watch.Load();
        Assert.Empty(settings.Monitored);
        Assert.Empty(settings.Webhook);
        Assert.Empty(settings.Ris.Hosts);
        Assert.Equal((60, 14), (settings.Rpki.RefreshMinutes, settings.History.RetentionDays));
        Assert.Null(Watch.LastWrite);
    }

    [Fact]
    public void TheExampleParses()
    {
        var settings = WatchFile.Parse(Example);
        Assert.Equal(["193.0.0.0/21", "2001:db8::/32"], settings.Monitored.Select(m => m.Prefix));
        Assert.Equal([64496, 64497], settings.Monitored[1].ExpectedOrigins);
        Assert.Equal("", settings.Monitored[1].Description);
        Assert.Equal(["rrc00", "rrc21"], settings.Ris.Hosts);
        Assert.Equal((30, 7), (settings.Rpki.RefreshMinutes, settings.History.RetentionDays));
        Assert.Equal([new WebhookEntry("ops", "https://example.org/hooks/bgp")], settings.Webhook);
    }

    [Fact]
    public void TheFileRoundTrips()
    {
        var written = WatchFile.Format(WatchFile.Parse(Example));
        Assert.Equal(written, WatchFile.Format(WatchFile.Parse(written)));
        Assert.Contains("[[monitored]]", written, StringComparison.Ordinal);
        Assert.Contains("[[webhook]]", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDetectorHoldsThePrefixesAndTheStaticRoas()
    {
        var detector = WatchFile.Parse(Example).ToDetector();
        Assert.Equal(2, detector.Monitored.Count);
        Assert.Equal([3333], detector.Monitored[0].ExpectedOrigins);
        var roa = Assert.Single(detector.Vrps);
        Assert.Equal(("193.0.0.0/21", 21, 3333L), (roa.Prefix.ToString(), roa.MaxLength, roa.OriginAs));
    }

    [Fact]
    public void AnUnknownTableIsIgnored()
    {
        var settings = WatchFile.Parse(Example + "\n[[events]]\nat = 2008-02-24T18:47:00Z\nlabel = \"x\"\n");
        Assert.Equal(2, settings.Monitored.Count);
    }

    [Fact]
    public async Task AddWritesTheEntryAndCreatesTheFolder()
    {
        var watch = Watch;
        var settings = await watch.AddAsync("193.0.6.139/21", [3333, 3333, 1], "RIPE NCC", CancellationToken.None);
        var entry = Assert.Single(settings.Monitored);
        Assert.Equal(("193.0.0.0/21", "RIPE NCC"), (entry.Prefix, entry.Description));
        Assert.Equal([1, 3333], entry.ExpectedOrigins);
        Assert.Equal("193.0.0.0/21", Assert.Single(watch.Load().Monitored).Prefix);
        Assert.NotNull(watch.LastWrite);
        Assert.Equal(["bgpwatch.toml"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AddReplacesTheEntryOfThePrefixInPlace()
    {
        var watch = Watch;
        await watch.AddAsync("193.0.0.0/21", [3333], "first", CancellationToken.None);
        await watch.AddAsync("198.51.100.0/24", [64496], "second", CancellationToken.None);
        var settings = await watch.AddAsync("193.0.0.0/21", [3333, 64500], "first again", CancellationToken.None);
        Assert.Equal(["193.0.0.0/21", "198.51.100.0/24"], settings.Monitored.Select(m => m.Prefix));
        Assert.Equal(("first again", 2), (settings.Monitored[0].Description, settings.Monitored[0].ExpectedOrigins.Length));
    }

    [Fact]
    public async Task RemoveDropsTheEntryAndLeavesTheRest()
    {
        var watch = Watch;
        await watch.AddAsync("193.0.0.0/21", [3333], "", CancellationToken.None);
        await watch.AddAsync("198.51.100.0/24", [64496], "", CancellationToken.None);
        var settings = await watch.RemoveAsync("193.0.0.0/21", CancellationToken.None);
        Assert.Equal("198.51.100.0/24", Assert.Single(settings.Monitored).Prefix);
        Assert.Single((await watch.RemoveAsync("10.0.0.0/8", CancellationToken.None)).Monitored);
    }

    [Fact]
    public async Task AnInvalidPrefixIsRefused()
    {
        await Assert.ThrowsAsync<FormatException>(() => Watch.AddAsync("example.org", [1], "", CancellationToken.None));
        await Assert.ThrowsAsync<FormatException>(() => Watch.RemoveAsync("193.0.0.0/33", CancellationToken.None));
    }

    [Fact]
    public async Task TheWebhooksSurviveAnEdit()
    {
        var watch = Watch;
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(watch.Location, Example, TestContext.Current.CancellationToken);
        var settings = await watch.AddAsync("10.0.0.0/8", [64500], "", CancellationToken.None);
        Assert.Equal(3, settings.Monitored.Count);
        Assert.Equal("ops", Assert.Single(watch.Load().Webhook).Name);
        Assert.Equal(["rrc00", "rrc21"], watch.Load().Ris.Hosts);
    }
}
