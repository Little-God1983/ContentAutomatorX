using System.Diagnostics;
using System.IO;
using System.Text;

namespace ContentAutomatorX.Infrastructure.Llm;

public class ProcessRunner : IProcessRunner
{
    // The no-BOM variant: Encoding.UTF8 writes a byte-order-mark preamble on the first write, which
    // would land as the opening bytes of stdin. Harmless to `claude -p` in practice, but there is no
    // reason to send it — the child process never asked for one.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
