using System.Net;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BGPWatch.Tests;

/// <summary>The incident desk: one notification when an incident opens, one when it closes, per source, through every notifier.</summary>
public class IncidentNotifierTests
{
    private static readonly IPNetwork Watched = IPNetwork.Parse("203.0.113.0/24");
    private static readonly DateTime Start = new(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);
    private const string Peer = "rrc21 192.0.2.1";

    private sealed class Recorder(string name = "recorder", bool failing = false) : INotifier
    {
        public List<Notification> Sent { get; } = [];

        public string Name => name;

        public Task NotifyAsync(Notification notification, CancellationToken cancel)
        {
            if (failing)
            {
                throw new InvalidOperationException("down");
            }
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private static Announcement Route(long origin, DateTime at)
    {
        return Announcement.Of(Watched, [64500, origin], Peer, "192.0.2.1", at);
    }

    private static Alert Raised(long origin, DateTime at)
    {
        return new Alert(AlertType.OriginHijack, Watched, origin, Peer, [64500, origin], $"203.0.113.0/24 announced by AS{origin}, expected AS65001",
            new HashSet<long> { 65001 }, null, at);
    }

    private static async Task<(IncidentNotifier Desk, Recorder Hook)> DeskAsync(params INotifier[] others)
    {
        var hook = new Recorder();
        var desk = new IncidentNotifier(NullLogger.Instance) { Notifiers = [.. others, hook] };
        await desk.WatchAsync("ris", [Watched], _ => true, CancellationToken.None);
        return (desk, hook);
    }

    [Fact]
    public async Task AnOpenedIncidentGoesOutOnceWithItsSourceAndText()
    {
        var (desk, hook) = await DeskAsync();
        await desk.OfferAsync("ris", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        var sent = Assert.Single(hook.Sent);
        Assert.Equal((NotificationKind.Opened, "ris", Start), (sent.Kind, sent.Source, sent.At));
        Assert.Equal("203.0.113.0/24 announced by AS65003, expected AS65001; seen by one vantage point", sent.Text);
        Assert.Equal("203.0.113.0/24", sent.Incident!.Prefix);
        await desk.OfferAsync("ris", [], [Raised(65003, Start.AddMinutes(1))], CancellationToken.None);
        Assert.Single(hook.Sent);
    }

    [Fact]
    public async Task AnIncidentClosesOnlyAfterItsRouteIsWithdrawn()
    {
        var (desk, hook) = await DeskAsync();
        await desk.OfferAsync("ris", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        await desk.SweepAsync(Start.AddHours(1), CancellationToken.None);
        Assert.Single(hook.Sent);
        await desk.OfferAsync("ris", [Announcement.Withdrawal(Watched, Peer, Start.AddHours(1))], [], CancellationToken.None);
        await desk.SweepAsync(Start.AddHours(2), CancellationToken.None);
        var closed = hook.Sent[1];
        Assert.Equal((NotificationKind.Closed, Start.AddHours(2)), (closed.Kind, closed.At));
        Assert.StartsWith("203.0.113.0/24 by AS65003 is over", closed.Text);
        Assert.Empty(desk.Open("ris"));
    }

    [Fact]
    public async Task AFailingNotifierHoldsUpNoOtherAndATestNamesIt()
    {
        var (desk, hook) = await DeskAsync(new Recorder("hook", failing: true));
        await desk.OfferAsync("ris", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        Assert.Single(hook.Sent);
        Assert.Equal(["hook"], await desk.TestAsync(Start, CancellationToken.None));
        Assert.Equal(2, hook.Sent.Count);
    }

    [Fact]
    public async Task AnOriginThatNoLongerOffendsClosesItsIncidentWhileItsRouteIsHeld()
    {
        var (desk, hook) = await DeskAsync();
        await desk.OfferAsync("ris", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        await desk.SweepAsync(Start.AddHours(1), CancellationToken.None);
        Assert.Single(hook.Sent);
        await desk.WatchAsync("ris", [Watched], _ => false, CancellationToken.None);
        await desk.SweepAsync(Start.AddHours(2), CancellationToken.None);
        Assert.Equal(NotificationKind.Closed, hook.Sent[1].Kind);
        Assert.Contains("no vantage point holds an offending route", hook.Sent[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePipelineJudgesARouteByTheGroundTruthInForce()
    {
        var watched = new MonitoredPrefix(Watched, new HashSet<long> { 65001 }, "lab");
        var pipeline = new Pipeline(new DetectorConfig([watched], []), new DiscardingRouteLog(), NullLogger.Instance, "ris");
        Assert.True(pipeline.Offends(Route(65003, Start)));
        pipeline.Reconfigure(new DetectorConfig([watched with { ExpectedOrigins = new HashSet<long> { 65001, 65003 } }], []));
        Assert.False(pipeline.Offends(Route(65003, Start)));
    }

    private sealed class Held(Task release) : INotifier
    {
        public TaskCompletionSource Started { get; } = new();

        public bool Done { get; private set; }

        public string Name => "held";

        public async Task NotifyAsync(Notification notification, CancellationToken cancel)
        {
            Started.TrySetResult();
            await release;
            Done = true;
        }
    }

    [Fact]
    public async Task WhileTheDispatcherRunsAnOfferDoesNotWaitForTheNotifiers()
    {
        var release = new TaskCompletionSource();
        var held = new Held(release.Task);
        var desk = new IncidentNotifier(NullLogger.Instance) { Notifiers = [held] };
        await desk.WatchAsync("ris", [Watched], _ => true, CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var dispatcher = desk.RunAsync(stop.Token);
        await desk.OfferAsync("ris", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(held.Done);
        release.SetResult();
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher);
        Assert.True(held.Done);
    }

    [Fact]
    public async Task ATestNotificationReachesEveryNotifier()
    {
        var (desk, hook) = await DeskAsync();
        Assert.Empty(await desk.TestAsync(Start, CancellationToken.None));
        var sent = Assert.Single(hook.Sent);
        Assert.Equal((NotificationKind.Test, "test notification from BGPWatch", Start), (sent.Kind, sent.Text, sent.At));
        Assert.Null(sent.Incident);
    }

    [Fact]
    public async Task EverySourceKeepsItsOwnIncidentsAndRoutes()
    {
        var (desk, hook) = await DeskAsync();
        await desk.WatchAsync("lab", [Watched], _ => true, CancellationToken.None);
        await desk.OfferAsync("ris", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        await desk.OfferAsync("lab", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        Assert.Equal(["ris", "lab"], hook.Sent.Select(n => n.Source));
        Assert.Single(desk.Open("lab"));
        Assert.Single(desk.Routes("ris"));
        Assert.Empty(desk.Routes("replay"));
    }

    [Fact]
    public async Task WatchingAgainRekeysWithoutLosingTheOpenIncidents()
    {
        var (desk, hook) = await DeskAsync();
        await desk.OfferAsync("ris", [Route(65003, Start)], [Raised(65003, Start)], CancellationToken.None);
        await desk.WatchAsync("ris", [IPNetwork.Parse("198.51.100.0/24")], _ => true, CancellationToken.None);
        Assert.Single(desk.Open("ris"));
        Assert.Empty(desk.Routes("ris"));
        await desk.SweepAsync(Start.AddHours(1), CancellationToken.None);
        Assert.Equal(NotificationKind.Closed, hook.Sent[1].Kind);
    }
}
