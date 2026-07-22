using ContentAutomatorX.Web.Services;

namespace ContentAutomatorX.IntegrationTests;

/// <summary>Behavioural coverage for the tenant-switch reentrancy guard (#16). The gate must
/// (a) never let two reloads run at once — the bug it exists to prevent is overlapping ops on the
/// one scoped DbContext — and (b) be latest-wins, not first-wins: a switch arriving mid-reload must
/// not be dropped, and after a burst the last requested reload is the one that ultimately runs.</summary>
public class TenantReloadGateTests
{
    [Fact]
    public async Task Reloads_never_run_concurrently_and_the_latest_supersedes_queued_stale_ones()
    {
        using var gate = new TenantReloadGate();
        var executed = new List<int>();
        var executedLock = new object();
        int concurrent = 0, maxConcurrent = 0;
        var maxLock = new object();

        var firstRunning = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();

        Func<int, TaskCompletionSource?, Task?, Func<Task>> reload =
            (id, started, release) => async () =>
            {
                var now = Interlocked.Increment(ref concurrent);
                lock (maxLock) maxConcurrent = Math.Max(maxConcurrent, now);
                started?.TrySetResult();
                if (release is not null) await release;
                lock (executedLock) executed.Add(id);
                Interlocked.Decrement(ref concurrent);
            };

        var t1 = gate.RunAsync(reload(1, firstRunning, releaseFirst.Task));
        await firstRunning.Task;                       // #1 is now inside the gate, holding it

        var t2 = gate.RunAsync(reload(2, null, null)); // queues behind the gate — generation 2
        var t3 = gate.RunAsync(reload(3, null, null)); // queues behind the gate — generation 3 (newest)

        releaseFirst.TrySetResult();                   // let #1 complete and release the gate
        await Task.WhenAll(t1, t2, t3);

        Assert.Equal(1, maxConcurrent);                // never overlapped — the core guarantee
        Assert.Contains(1, executed);                  // the already-running reload completed
        Assert.Contains(3, executed);                  // the newest queued reload ran
        Assert.DoesNotContain(2, executed);            // the superseded queued reload was skipped
        lock (executedLock) Assert.Equal(3, executed[^1]); // final render belongs to the latest switch
    }

    [Fact]
    public async Task A_reload_requested_during_an_in_flight_reload_is_not_dropped()
    {
        // Contrast with a first-wins `if (_reloading) return;` guard, which would silently drop the
        // second request and leave the page on the first tenant's data.
        using var gate = new TenantReloadGate();
        var executed = new List<int>();
        var executedLock = new object();

        var firstRunning = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();

        var t1 = gate.RunAsync(async () =>
        {
            firstRunning.TrySetResult();
            await releaseFirst.Task;
            lock (executedLock) executed.Add(1);
        });
        await firstRunning.Task;

        var t2 = gate.RunAsync(() =>
        {
            lock (executedLock) executed.Add(2);
            return Task.CompletedTask;
        });

        releaseFirst.TrySetResult();
        await Task.WhenAll(t1, t2);

        Assert.Equal([1, 2], executed);   // both ran, in order — the mid-flight request survived
    }

    [Fact]
    public async Task A_single_reload_runs_to_completion()
    {
        using var gate = new TenantReloadGate();
        var ran = false;
        await gate.RunAsync(() => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
    }

    [Fact]
    public async Task RunAsync_after_dispose_is_a_safe_no_op()
    {
        var gate = new TenantReloadGate();
        gate.Dispose();

        var ran = false;
        // Must not throw ObjectDisposedException back to the fire-and-forget caller.
        await gate.RunAsync(() => { ran = true; return Task.CompletedTask; });

        Assert.False(ran);
    }
}
