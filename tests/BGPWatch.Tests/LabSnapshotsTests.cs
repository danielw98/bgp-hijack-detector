using BGPWatch.Web.Lab;

namespace BGPWatch.Tests;

public class LabSnapshotsTests
{
    private TimeSpan now = TimeSpan.FromMinutes(1);
    private int taken;

    private LabSnapshots Snapshots()
    {
        return new LabSnapshots(() =>
        {
            taken++;
            return Task.FromResult(new List<RouterState>());
        }, () => now);
    }

    [Fact]
    public async Task TheFirstReadTakesASnapshot()
    {
        await Snapshots().CurrentAsync(CancellationToken.None);
        Assert.Equal(1, taken);
    }

    [Fact]
    public async Task AReadWithinMaxAgeSharesTheLastSnapshot()
    {
        var snapshots = Snapshots();
        await snapshots.CurrentAsync(CancellationToken.None);
        now += LabSnapshots.MaxAge - TimeSpan.FromMilliseconds(1);
        await snapshots.CurrentAsync(CancellationToken.None);
        Assert.Equal(1, taken);
    }

    [Fact]
    public async Task AReadAtMaxAgeTakesANewSnapshot()
    {
        var snapshots = Snapshots();
        await snapshots.CurrentAsync(CancellationToken.None);
        now += LabSnapshots.MaxAge;
        await snapshots.CurrentAsync(CancellationToken.None);
        Assert.Equal(2, taken);
    }

    [Fact]
    public async Task AReadAfterExpireTakesANewSnapshot()
    {
        var snapshots = Snapshots();
        await snapshots.CurrentAsync(CancellationToken.None);
        snapshots.Expire();
        await snapshots.CurrentAsync(CancellationToken.None);
        Assert.Equal(2, taken);
    }

    [Fact]
    public async Task ConcurrentReadsWaitForOneSnapshot()
    {
        var release = new TaskCompletionSource<List<RouterState>>();
        var snapshots = new LabSnapshots(() =>
        {
            taken++;
            return release.Task;
        }, () => now);
        var reads = Enumerable.Range(0, 5).Select(_ => snapshots.CurrentAsync(CancellationToken.None)).ToList();
        release.SetResult([]);
        await Task.WhenAll(reads);
        Assert.Equal(1, taken);
    }

    [Fact]
    public async Task AnExpireDuringASnapshotMakesTheNextReadTakeAnother()
    {
        var release = new TaskCompletionSource<List<RouterState>>();
        var snapshots = new LabSnapshots(() =>
        {
            taken++;
            return taken == 1 ? release.Task : Task.FromResult(new List<RouterState>());
        }, () => now);
        var first = snapshots.CurrentAsync(CancellationToken.None);
        snapshots.Expire();
        release.SetResult([]);
        await first;
        await snapshots.CurrentAsync(CancellationToken.None);
        Assert.Equal(2, taken);
    }
}
