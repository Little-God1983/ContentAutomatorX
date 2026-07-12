namespace ContentAutomatorX.Infrastructure.Llm;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default);
}
