namespace ContentAutomatorX.Infrastructure.Llm;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default);

    /// <summary>Runs the process and yields stdout lines as they arrive, instead of returning the
    /// whole output at exit. Existing <see cref="RunAsync"/> callers are unaffected.
    ///
    /// <para><paramref name="idleTimeout"/> is an <i>idle</i> limit — the maximum gap between lines,
    /// not a wall-clock cap — so a long but steadily-progressing run is not mistaken for a hang.
    /// A stall longer than it throws <see cref="System.TimeoutException"/>. A non-zero exit throws
    /// <see cref="System.InvalidOperationException"/> with stderr. Cancelling <paramref name="ct"/>
    /// or abandoning the enumeration kills the whole process tree.</para></summary>
    IAsyncEnumerable<string> RunStreamingAsync(string fileName, string arguments, string? stdin,
        TimeSpan idleTimeout, CancellationToken ct = default);
}
