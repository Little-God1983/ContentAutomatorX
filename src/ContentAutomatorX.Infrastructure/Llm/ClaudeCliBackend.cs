using System.Text.Json;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Llm;

public class ClaudeCliOptions
{
    /// <summary>Executable name/path. On Windows, if plain "claude" fails to start,
    /// set the full path (e.g. %LOCALAPPDATA%\...\claude.exe) in appsettings Claude:Command.</summary>
    public string Command { get; set; } = "claude";
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
}

public class ClaudeCliBackend(IProcessRunner runner, ClaudeCliOptions options) : ILlmBackend
{
    public string Name => "claude-cli";

    public async Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var args = "-p --output-format json";
        if (!string.IsNullOrWhiteSpace(options.Model)) args += $" --model {options.Model}";
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        string lastError = "";
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var result = await runner.RunAsync(options.Command, args, prompt, timeout, ct);
            if (result.ExitCode == 0 && TryParse(result.StdOut, out var text))
                return new LlmResult(text, options.Model ?? "claude-default");
            lastError = $"exit={result.ExitCode} stderr={result.StdErr} stdout={Truncate(result.StdOut)}";
        }
        throw new InvalidOperationException($"claude CLI failed after 2 attempts: {lastError}");
    }

    private static bool TryParse(string stdout, out string text)
    {
        text = "";
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("is_error", out var e) && e.GetBoolean()) return false;
            if (!root.TryGetProperty("result", out var r)) return false;
            text = r.GetString() ?? "";
            return text.Length > 0;
        }
        catch (JsonException) { return false; }
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}
