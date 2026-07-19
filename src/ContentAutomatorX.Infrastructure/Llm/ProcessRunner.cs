using System.Diagnostics;
using System.IO;

namespace ContentAutomatorX.Infrastructure.Llm;

public class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default)
    {
        // On Windows, resolve npm-style .cmd/.bat shims (e.g. the "claude" CLI) the way
        // a shell would — a raw CreateProcess only finds .exe on PATH and cannot launch
        // batch files, so those get routed through cmd.exe. See WindowsCommandResolver.
        if (OperatingSystem.IsWindows())
            (fileName, arguments) = WindowsCommandResolver.Resolve(fileName, arguments, File.Exists, PathDirs(), PathExts());

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* observe, discard */ }
            return new ProcessResult(-1, "", $"timed out after {timeout.TotalSeconds}s");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static IEnumerable<string> PathDirs() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> PathExts() =>
        (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
