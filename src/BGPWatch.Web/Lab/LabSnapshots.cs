namespace BGPWatch.Web.Lab;

/// <summary>
/// The last lab snapshot, shared by every reader: a read younger than MaxAge returns it,
/// an older one takes a new snapshot, and readers that arrive meanwhile wait for that one.
/// The cost of the lab view is one snapshot per MaxAge, whatever the number of open tabs.
/// </summary>
public sealed class LabSnapshots(Func<Task<List<RouterState>>> take, Func<TimeSpan> clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1.5);

    private readonly SemaphoreSlim gate = new(1, 1);
    private List<RouterState> last = [];
    private TimeSpan? takenAt;
    private int changes;
    private int changesAtTake;

    /// <summary>
    /// Snapshots read through Docker, with the routers whose BMP session is up at the
    /// time; no reader's cancellation stops a snapshot that others wait for.
    /// </summary>
    public static LabSnapshots Over(DockerEngine docker, Bmp.BmpStation station, Func<TimeSpan> clock)
    {
        return new LabSnapshots(() => LabView.SnapshotAsync(
            async (container, command) => (await docker.ExecAsync(container, command, CancellationToken.None)).Stdout,
            station.Reporting), clock);
    }

    /// <summary>A snapshot no older than MaxAge.</summary>
    public async Task<List<RouterState>> CurrentAsync(CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            if (takenAt is not { } at || clock() - at >= MaxAge || changesAtTake != Volatile.Read(ref changes))
            {
                var changesBefore = Volatile.Read(ref changes);
                last = await take();
                takenAt = clock();
                changesAtTake = changesBefore;
            }
            return last;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Makes the next read take a new snapshot, after a change to the lab.</summary>
    public void Expire()
    {
        Interlocked.Increment(ref changes);
    }
}
