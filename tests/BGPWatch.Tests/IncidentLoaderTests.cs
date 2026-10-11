using System.Net;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BGPWatch.Tests;

/// <summary>The loader stores an incident once, a file per transaction, under its source name, and replaces it only when forced.</summary>
public class IncidentLoaderTests
{
    private const string Source = "youtube-2008";
    private static readonly DateTime Seen = new(2008, 2, 24, 18, 47, 57, DateTimeKind.Utc);
    private static readonly DetectorConfig Config = new(
        [new MonitoredPrefix(IPNetwork.Parse("208.65.152.0/22"), new HashSet<int> { 36561 }, "YouTube")], []);

    private sealed class FakeLog(SourceCounts stored) : IRouteLog
    {
        public List<(IReadOnlyList<Announcement> Updates, IReadOnlyList<Alert> Alerts, string Source)> Appended { get; } = [];

        public List<string> Deleted { get; } = [];

        public List<MonitoredPrefix> Synced { get; } = [];

        public Task SyncMonitoredAsync(IEnumerable<MonitoredPrefix> monitored, CancellationToken cancel)
        {
            Synced.AddRange(monitored);
            return Task.CompletedTask;
        }

        public Task AppendAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel)
        {
            Appended.Add((updates, alerts, source));
            return Task.CompletedTask;
        }

        public Task<SourceCounts> CountAsync(string source, CancellationToken cancel)
        {
            return Task.FromResult(stored);
        }

        public Task DeleteAsync(string source, CancellationToken cancel)
        {
            Deleted.Add(source);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeArchive(int routesPerFile) : IUpdateArchive
    {
        public ArchiveFile Read(string path)
        {
            var legit = Announcement.Of(IPNetwork.Parse("208.65.152.0/22"), [3333, 36561], "193.0.0.56", null, Seen);
            var hijack = Announcement.Of(IPNetwork.Parse("208.65.153.0/24"), [3333, 3491, 17557], "193.0.0.56", null, Seen);
            return new ArchiveFile([.. Enumerable.Range(0, routesPerFile).Select(i => i == 0 ? hijack : legit)], 0);
        }
    }

    private static Task<LoadSummary> LoadAsync(FakeLog log, string[] files, bool force, int routesPerFile = 3)
    {
        var loader = new IncidentLoader(log, new FakeArchive(routesPerFile), NullLogger.Instance);
        return loader.LoadAsync(Config, Source, files, force, CancellationToken.None);
    }

    [Fact]
    public async Task ALoadedSourceIsLeftAloneUnlessForced()
    {
        var log = new FakeLog(new SourceCounts(3258, 27));
        var summary = await LoadAsync(log, ["x.gz"], force: false);
        Assert.Equal(new LoadSummary(Source, 3258, 27, true), summary);
        Assert.Empty(log.Appended);
        Assert.Empty(log.Deleted);
    }

    [Fact]
    public async Task AForcedLoadReplacesTheSource()
    {
        var log = new FakeLog(new SourceCounts(3258, 27));
        var summary = await LoadAsync(log, ["x.gz"], force: true);
        Assert.Equal([Source], log.Deleted);
        Assert.Equal(new LoadSummary(Source, 3, 1, false), summary);
    }

    [Fact]
    public async Task AnEmptySourceIsLoadedOneFilePerBatchTaggedWithTheSource()
    {
        var log = new FakeLog(new SourceCounts(0, 0));
        var summary = await LoadAsync(log, ["a.gz", "b.gz"], force: false, routesPerFile: 1005);
        Assert.Equal([(1005, 1), (1005, 1)], log.Appended.Select(a => (a.Updates.Count, a.Alerts.Count)));
        Assert.All(log.Appended, a => Assert.Equal(Source, a.Source));
        Assert.Equal(new LoadSummary(Source, 2010, 2, false), summary);
    }

    [Fact]
    public async Task TheMonitoredPrefixesAreSyncedBeforeLoading()
    {
        var log = new FakeLog(new SourceCounts(0, 0));
        await LoadAsync(log, [], force: false);
        Assert.Equal("208.65.152.0/22", Assert.Single(log.Synced).Prefix.ToString());
    }

    private sealed class HeldLog(TaskCompletionSource release) : IRouteLog
    {
        public int Counts { get; private set; }

        public Task SyncMonitoredAsync(IEnumerable<MonitoredPrefix> monitored, CancellationToken cancel)
        {
            return Task.CompletedTask;
        }

        public Task AppendAsync(IReadOnlyList<Announcement> updates, IReadOnlyList<Alert> alerts, string source, CancellationToken cancel)
        {
            return release.Task;
        }

        public Task<SourceCounts> CountAsync(string source, CancellationToken cancel)
        {
            Counts++;
            return Task.FromResult(new SourceCounts(0, 0));
        }

        public Task DeleteAsync(string source, CancellationToken cancel)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ASecondLoadWaitsForTheFirstToFinish()
    {
        var release = new TaskCompletionSource();
        var log = new HeldLog(release);
        var loader = new IncidentLoader(log, new FakeArchive(1), NullLogger.Instance);
        var first = loader.LoadAsync(Config, Source, ["a.gz"], force: true, CancellationToken.None);
        var second = loader.LoadAsync(Config, Source, ["a.gz"], force: true, CancellationToken.None);
        Assert.Equal(1, log.Counts);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, log.Counts);
    }

    [Fact]
    public async Task AnAlertCarriesTheCoveringPrefixAndThePath()
    {
        var log = new FakeLog(new SourceCounts(0, 0));
        await LoadAsync(log, ["a.gz"], force: false, routesPerFile: 1);
        var alert = Assert.Single(log.Appended[0].Alerts);
        Assert.Equal((AlertType.SubprefixHijack, "208.65.153.0/24", "208.65.152.0/22"), (alert.Type, alert.Prefix.ToString(), alert.CoveringPrefix.ToString()));
        Assert.Equal([3333, 3491, 17557], alert.AsPath);
    }
}
