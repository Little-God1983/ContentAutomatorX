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
}
