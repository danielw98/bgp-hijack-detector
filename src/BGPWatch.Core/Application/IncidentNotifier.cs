using System.Net;
using System.Threading.Channels;
using BGPWatch.Core.Domain;
using Microsoft.Extensions.Logging;

namespace BGPWatch.Core.Application;

/// <summary>
/// Where the alerts of every source become notifications: one when an incident opens,
/// one when it closes, through every notifier of the moment. Each source has its own
/// incidents, its own table of the routes its vantage points hold, and its own judge of
/// whether a held route still offends; an incident closes once it was quiet for the gap
/// and no vantage point holds an offending route. While RunAsync runs, the notifications
/// go out from it in order, so the ingest never waits on a slow notifier; without it they
/// go out inline. A notifier that fails is logged by its name and holds up nothing else.
/// </summary>
public sealed class IncidentNotifier(ILogger logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Watched> sources = new(StringComparer.Ordinal);
    private readonly Channel<Notification> outbox = Channel.CreateUnbounded<Notification>(new UnboundedChannelOptions { SingleReader = true });
    private volatile IReadOnlyList<INotifier> notifiers = [];
    private volatile bool dispatching;

    public IReadOnlyList<INotifier> Notifiers
    {
        get => notifiers;
        set => notifiers = value;
    }

    /// <summary>
    /// Watches a source for these monitored prefixes, or changes them; <paramref name="offends"/>
    /// tells whether a held route would raise an alert under the ground truth in force.
    /// </summary>
    public async Task WatchAsync(string source, IEnumerable<IPNetwork> monitored, Func<Announcement, bool> offends, CancellationToken cancel)
    {
        var networks = monitored.ToList();
        await gate.WaitAsync(cancel);
        try
        {
            if (sources.TryGetValue(source, out var watched))
            {
                watched.Rekey(networks, offends);
            }
            else
            {
                sources[source] = new Watched(networks, offends);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Files what a source observed, the routes into its table and the alerts into its incidents, and notifies the incidents that opened.</summary>
    public async Task OfferAsync(string source, IReadOnlyList<Announcement> announcements, IReadOnlyList<Alert> alerts, CancellationToken cancel)
    {
        List<Notification> opened;
        await gate.WaitAsync(cancel);
        try
        {
            var watched = Of(source);
            watched.Routes.Apply(announcements);
            opened = [.. watched.Incidents.Offer(alerts)
                .Where(e => e.Change == IncidentChange.Opened)
                .Select(e => Notification.Opened(source, e.Incident))];
        }
        finally
        {
            gate.Release();
        }
        await SendAsync(opened, cancel);
    }

    /// <summary>Closes and notifies the incidents of every source that were quiet for the gap and whose offending routes are gone.</summary>
    public async Task SweepAsync(DateTime now, CancellationToken cancel)
    {
        var closed = new List<Notification>();
        await gate.WaitAsync(cancel);
        try
        {
            foreach (var (source, watched) in sources)
            {
                closed.AddRange(watched.Incidents.Sweep(now, watched.Persists).Select(e => Notification.Closed(source, e.Incident, now)));
            }
        }
        finally
        {
            gate.Release();
        }
        await SendAsync(closed, cancel);
    }

    /// <summary>One test notification per notifier, sent at once; returns the names of those that failed.</summary>
    public Task<IReadOnlyList<string>> TestAsync(DateTime now, CancellationToken cancel)
    {
        return NotifyAsync(Notification.Test(now), cancel);
    }

    /// <summary>Sends the queued notifications in order until stopped; while it runs, offers and sweeps only queue them.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        dispatching = true;
        try
        {
            await foreach (var notification in outbox.Reader.ReadAllAsync(stop))
            {
                await NotifyAsync(notification, stop);
            }
        }
        finally
        {
            dispatching = false;
        }
    }

    public IReadOnlyList<OpenIncident> Open(string source)
    {
        return Under(source, watched => watched.Incidents.Open);
    }

    public IReadOnlyList<Announcement> Routes(string source)
    {
        return Under(source, watched => watched.Routes.Routes);
    }

    private T Under<T>(string source, Func<Watched, T> read)
    {
        gate.Wait();
        try
        {
            return read(Of(source));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The source's incidents and routes; a source never watched holds every route it reports against its origin.</summary>
    private Watched Of(string source)
    {
        if (!sources.TryGetValue(source, out var watched))
        {
            watched = new Watched([], _ => true);
            sources[source] = watched;
        }
        return watched;
    }

    private async Task SendAsync(IReadOnlyList<Notification> notifications, CancellationToken cancel)
    {
        foreach (var notification in notifications)
        {
            if (dispatching)
            {
                outbox.Writer.TryWrite(notification);
            }
            else
            {
                await NotifyAsync(notification, cancel);
            }
        }
    }

    private async Task<IReadOnlyList<string>> NotifyAsync(Notification notification, CancellationToken cancel)
    {
        var failed = new List<string>();
        foreach (var notifier in notifiers)
        {
            try
            {
                await notifier.NotifyAsync(notification, cancel);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "notifier {Name} failed on {Kind}", notifier.Name, notification.Kind);
                failed.Add(notifier.Name);
            }
        }
        return failed;
    }

    /// <summary>One source: its incidents, the routes its vantage points hold, and the judge of a held route.</summary>
    private sealed class Watched
    {
        private Func<Announcement, bool> offends;

        public Watched(List<IPNetwork> monitored, Func<Announcement, bool> offends)
        {
            Incidents = new IncidentTracker(monitored);
            Routes = new RouteTable(monitored);
            this.offends = offends;
        }

        public IncidentTracker Incidents { get; }

        public RouteTable Routes { get; }

        public void Rekey(List<IPNetwork> monitored, Func<Announcement, bool> judge)
        {
            Incidents.Rekey(monitored);
            Routes.Rekey(monitored);
            offends = judge;
        }

        /// <summary>Whether a vantage point still holds a route inside the prefix, from the origin, that would raise an alert now.</summary>
        public bool Persists(string prefix, long? origin)
        {
            return Routes.Within(IPNetwork.Parse(prefix)).Any(r => r.OriginAs == origin && offends(r));
        }
    }
}
