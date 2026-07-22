namespace ContentAutomatorX.Web.Services;

/// <summary>
/// Serialises tenant-triggered reloads for a single page so they never overlap on the circuit's
/// one scoped <c>AppDbContext</c> (which is not thread-safe and throws "A second operation was
/// started on this context instance" if two operations interleave), and guarantees <b>latest-wins</b>:
/// when a burst of tenant switches arrives faster than a reload completes, the data ultimately
/// applied belongs to the <i>last</i> switch, never the first and never a mix.
///
/// <para>Why not the obvious guard. A <c>if (_reloading) return;</c> flag is first-wins: a user
/// clicking tenant A then quickly B would have B dropped, leaving the page rendering A's data while
/// the active tenant is already B — one tenant's data shown under another's identity. Instead each
/// call takes a monotonic generation number; after acquiring the gate a call bails out if a newer
/// generation has since been requested, so only the most recent reload does the DB work.</para>
///
/// <para>The <see cref="SemaphoreSlim"/> is genuinely required: Blazor's single-threaded circuit
/// context does not serialise these handlers, because <c>await</c> yields the context and lets the
/// next queued reload start mid-flight. A plain bool set/reset around awaits would be racy.</para>
///
/// <para>Not a <c>ComponentBase</c> on purpose: the pages' pre-reload steps differ (some reset
/// fields, AI Studio blanks its card and disables Save on failure), and a base class would hide the
/// <c>OnInitializedAsync</c>/<c>Dispose</c> lifecycle each page already owns. This wraps only the
/// reload body; the page keeps its own lifecycle and per-page logic.</para>
/// </summary>
public sealed class TenantReloadGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _generation;
    private volatile bool _disposed;

    /// <summary>
    /// Runs <paramref name="reload"/> under the gate. If a newer <see cref="RunAsync"/> is requested
    /// before this one acquires the gate, this call is superseded and <paramref name="reload"/> is
    /// skipped. Never runs concurrently with another <see cref="RunAsync"/> on the same gate.
    /// </summary>
    /// <remarks>Deliberately does not use <c>ConfigureAwait(false)</c>: callers invoke it on the
    /// Blazor circuit's synchronization context (via <c>InvokeAsync</c> or the component lifecycle)
    /// and the wrapped reload touches the scoped DbContext and calls <c>StateHasChanged</c>, both of
    /// which must stay on that context.</remarks>
    public async Task RunAsync(Func<Task> reload)
    {
        var generation = Interlocked.Increment(ref _generation);

        try
        {
            await _gate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return; // RunAsync was called after the gate (page/circuit) was disposed
        }

        try
        {
            // A newer switch superseded us while we waited — let it own the final render.
            if (_disposed || generation != Volatile.Read(ref _generation))
                return;

            await reload();
        }
        finally
        {
            // Dispose may have run mid-reload (circuit teardown); releasing a disposed
            // semaphore would surface as an unobserved task exception, so swallow it.
            try { _gate.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _gate.Dispose();
    }
}
