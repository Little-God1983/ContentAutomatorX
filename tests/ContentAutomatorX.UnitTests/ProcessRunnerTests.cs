using System.Text;
using ContentAutomatorX.Infrastructure.Llm;

namespace ContentAutomatorX.UnitTests;

/// <summary>
/// Exercises the real <see cref="Process"/>-spawning behavior of <see cref="ProcessRunner"/> —
/// unlike <see cref="ClaudeCliBackendTests"/>, which mocks at the <see cref="IProcessRunner"/>
/// boundary and never starts a child process, so it cannot see anything below that seam.
/// </summary>
public class ProcessRunnerTests
{
    private static readonly string PowerShellExe =
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    [WindowsOnlyFact]
    public async Task RunAsync_with_null_stdin_completes_instead_of_throwing()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync("cmd.exe", "/c exit 0", stdin: null, TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
    }

    [WindowsOnlyFact]
    public async Task Non_ascii_prompt_round_trips_unchanged_through_stdin_and_stdout()
    {
        // Em dash + curly double/single quotes: exactly the characters that get best-fit-substituted
        // to ASCII look-alikes (or mojibaked on the way back) under the console's OEM code page.
        const string nonAscii = "“Progress” — it’s real";

        // A minimal UTF-8 echo: read all of stdin as UTF-8, write it back to stdout as UTF-8.
        // Goes straight at the raw std handles via StreamReader/StreamWriter instead of
        // [Console]::InputEncoding/OutputEncoding — those setters shell out to SetConsoleCP/
        // SetConsoleOutputCP, which throw when the process has no real console (as here, where
        // all three std streams are redirected pipes and CreateNoWindow is set).
        const string script = """
            $in = [Console]::OpenStandardInput()
            $reader = New-Object System.IO.StreamReader($in, (New-Object System.Text.UTF8Encoding($false)))
            $text = $reader.ReadToEnd()
            $out = [Console]::OpenStandardOutput()
            $writer = New-Object System.IO.StreamWriter($out, (New-Object System.Text.UTF8Encoding($false)))
            $writer.Write($text)
            $writer.Flush()
            """;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}";

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(PowerShellExe, arguments, nonAscii, TimeSpan.FromSeconds(15));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(nonAscii, result.StdOut);
    }

    private static string PsArgs(string script) =>
        $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}";

    [WindowsOnlyFact]
    public async Task RunStreamingAsync_yields_lines_incrementally_as_they_are_written()
    {
        // Three lines 400 ms apart: if they were buffered to exit they would all arrive together.
        const string script = "Write-Output 'a'; Start-Sleep -Milliseconds 400; " +
                              "Write-Output 'b'; Start-Sleep -Milliseconds 400; Write-Output 'c'";
        var runner = new ProcessRunner();
        var lines = new List<string>();
        var stamps = new List<long>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await foreach (var line in runner.RunStreamingAsync(PowerShellExe, PsArgs(script), null, TimeSpan.FromSeconds(20)))
        {
            lines.Add(line);
            stamps.Add(sw.ElapsedMilliseconds);
        }

        Assert.Equal(["a", "b", "c"], lines);
        Assert.True(stamps[^1] - stamps[0] >= 400, $"expected incremental arrival; span was {stamps[^1] - stamps[0]}ms");
    }

    [WindowsOnlyFact]
    public async Task RunStreamingAsync_throws_TimeoutException_when_output_stalls_past_the_idle_limit()
    {
        var runner = new ProcessRunner();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var _ in runner.RunStreamingAsync(
                PowerShellExe, PsArgs("Start-Sleep -Seconds 5"), null, TimeSpan.FromSeconds(1)))
            { }
        });
    }

    [WindowsOnlyFact]
    public async Task RunStreamingAsync_throws_on_a_nonzero_exit_after_yielding_output()
    {
        var runner = new ProcessRunner();
        var lines = new List<string>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var line in runner.RunStreamingAsync(
                PowerShellExe, PsArgs("Write-Output 'x'; exit 3"), null, TimeSpan.FromSeconds(20)))
                lines.Add(line);
        });

        Assert.Equal(["x"], lines);
        Assert.Contains("exited 3", ex.Message);
    }

    [WindowsOnlyFact]
    public async Task RunStreamingAsync_cancellation_stops_the_stream_before_completion()
    {
        const string script = "1..50 | ForEach-Object { Write-Output $_; Start-Sleep -Milliseconds 150 }";
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();
        var lines = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var line in runner.RunStreamingAsync(
                PowerShellExe, PsArgs(script), null, TimeSpan.FromSeconds(30), cts.Token))
            {
                lines.Add(line);
                if (lines.Count >= 2) cts.Cancel();
            }
        });

        Assert.True(lines.Count < 50, $"expected early stop; got {lines.Count} lines");
    }

    [WindowsOnlyFact]
    public async Task RunStreamingAsync_pipes_stdin_and_reads_it_back()
    {
        const string input = "streamed “input” — ok";
        const string script = """
            $in = [Console]::OpenStandardInput()
            $reader = New-Object System.IO.StreamReader($in, (New-Object System.Text.UTF8Encoding($false)))
            $text = $reader.ReadToEnd()
            $out = [Console]::OpenStandardOutput()
            $writer = New-Object System.IO.StreamWriter($out, (New-Object System.Text.UTF8Encoding($false)))
            $writer.WriteLine($text)
            $writer.Flush()
            """;
        var runner = new ProcessRunner();
        var lines = new List<string>();

        await foreach (var line in runner.RunStreamingAsync(PowerShellExe, PsArgs(script), input, TimeSpan.FromSeconds(20)))
            lines.Add(line);

        Assert.Equal([input], lines);
    }
}
