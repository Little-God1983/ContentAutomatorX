using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace ContentAutomatorX.Infrastructure.Llm;

public class ProcessRunner : IProcessRunner
{
    // The no-BOM variant: Encoding.UTF8 writes a byte-order-mark preamble on the first write, which
    // would land as the opening bytes of stdin. Harmless to `claude -p` in practice, but there is no
    // reason to send it — the child process never asked for one.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private ProcessStartInfo BuildStartInfo(string fileName, string arguments, bool redirectStdin)
    {
        // Same resolution + UTF-8 handling as RunAsync; see the comments there for why each is needed.
        if (OperatingSystem.IsWindows())
            (fileName, arguments) = WindowsCommandResolver.Resolve(fileName, arguments, File.Exists, PathDirs(), PathExts());

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = redirectStdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            StandardInputEncoding = redirectStdin ? Utf8NoBom : null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(string fileName, string arguments, string? stdin,
        TimeSpan idleTimeout, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = BuildStartInfo(fileName, arguments, stdin is not null) };
        process.Start();

        // Drain stderr concurrently so a chatty stderr can't fill its pipe buffer and wedge the child.
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        // Begin consuming stdout before writing stdin: a large prompt could otherwise fill the child's
        // stdout pipe while it waits for us to read, and we'd deadlock waiting to finish writing stdin.
        var writeStdin = stdin is null ? Task.CompletedTask : Task.Run(async () =>
        {
            try { await process.StandardInput.WriteAsync(stdin.AsMemory(), ct); }
            finally { process.StandardInput.Close(); }
        }, ct);

        var reachedEof = false;
        try
        {
            while (true)
            {
                var line = await ReadLineOrIdleOutAsync(process, process.StandardOutput, idleTimeout, ct);
                if (line is null) break;   // stdout EOF
                yield return line;
            }
            reachedEof = true;   // normal completion: the process is exiting on its own, do not kill it
        }
        finally
        {
            // Abnormal exit only — ct cancellation, an early break/dispose of the enumerator, or an
            // exception above: kill the whole tree (on Windows the CLI runs under a cmd.exe shim, so
            // the claude process is a grandchild — entireProcessTree is load-bearing). On a clean EOF
            // we must NOT kill: that would pre-empt the child's own exit and lose its exit code.
            if (!reachedEof && !process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
            try { await writeStdin; } catch { }
            try { await stderrTask; } catch { }
        }

        // Reached EOF without cancellation — confirm a clean exit or surface the failure.
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            // stderrTask was already observed in the finally; if the drain itself faulted, report the
            // exit code with empty stderr rather than letting the IOException mask the real failure.
            var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
            throw new InvalidOperationException($"process exited {process.ExitCode}: {stderr}");
        }
    }

    /// <summary>Reads one line, but treats a gap longer than <paramref name="idleTimeout"/> as a hang
    /// and throws <see cref="TimeoutException"/> (after killing the tree). Returns null at EOF.
    /// A genuine caller cancellation (<paramref name="ct"/>) propagates as
    /// <see cref="OperationCanceledException"/> rather than being reported as an idle timeout.</summary>
    private static async Task<string?> ReadLineOrIdleOutAsync(Process process, StreamReader reader,
        TimeSpan idleTimeout, CancellationToken ct)
    {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idleCts.CancelAfter(idleTimeout);
        try
        {
            return await reader.ReadLineAsync(idleCts.Token);
        }
        catch (OperationCanceledException) when (idleCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"process produced no output for {idleTimeout.TotalSeconds}s");
        }
    }

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
            // Without these, .NET decodes the child's redirected streams using the console's OEM
            // code page on Windows (e.g. CP437) instead of UTF-8. The claude CLI's `--output-format
            // json` is UTF-8 (JSON always is), so anything outside ASCII — em dashes, curly quotes,
            // accented names — came back mojibake (an em dash arrived as "ΓÇö"). Our own prompts can
            // carry non-ASCII too (tenant voice profile, source content), so stdin gets the same fix.
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            // Unlike the two above, this one must mirror RedirectStandardInput's condition exactly:
            // .NET throws InvalidOperationException at Process.Start() if StandardInputEncoding is
            // set while standard input is not redirected (i.e. stdin is null).
            StandardInputEncoding = stdin is not null ? Utf8NoBom : null,
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
