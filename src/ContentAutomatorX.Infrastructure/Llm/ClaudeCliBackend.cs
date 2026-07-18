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

    /// <summary>Appended verbatim to the CLI args. E.g. "--allowedTools WebSearch" lets
    /// LlmResearch sources actually search the web. Configured via appsettings Claude:ExtraArgs.</summary>
    public string? ExtraArgs { get; set; }
}

public class ClaudeCliBackend(IProcessRunner runner, ClaudeCliOptions options) : ILlmBackend
{
    public string Name => "claude-cli";

    public async Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var args = "-p --output-format json";
        if (!string.IsNullOrWhiteSpace(options.Model)) args += $" --model {options.Model}";
        if (!string.IsNullOrWhiteSpace(options.ExtraArgs)) args += $" {options.ExtraArgs}";
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        string lastError = "";
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var result = await runner.RunAsync(options.Command, args, prompt, timeout, ct);
            if (result.ExitCode == 0 && TryParse(result.StdOut, out var text, out var model))
                return new LlmResult(text, model ?? options.Model ?? "claude-default");
            lastError = $"exit={result.ExitCode} stderr={result.StdErr} stdout={Truncate(result.StdOut)}";
        }
        throw new InvalidOperationException($"claude CLI failed after 2 attempts: {lastError}");
    }

    private static bool TryParse(string stdout, out string text, out string? model)
    {
        text = "";
        model = null;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("is_error", out var e) && e.GetBoolean()) return false;
            if (!root.TryGetProperty("result", out var r)) return false;
            text = r.GetString() ?? "";

            // Real CLI output (--output-format json) has no top-level "model" field; the
            // model actually used is the (sole) key of "modelUsage". Support a root "model"
            // string too in case a future CLI version adds one.
            if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                model = m.GetString();
            else if (root.TryGetProperty("modelUsage", out var mu) && mu.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in mu.EnumerateObject())
                {
                    model = prop.Name;
                    break;
                }
            }

            return text.Length > 0;
        }
        catch (JsonException) { return false; }
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}
