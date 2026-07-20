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

    /// <summary>Fallback reasoning depth for tenants that have not chosen one.
    /// Storage vocabulary: "", low, medium, high, xhigh, max. Configured via
    /// appsettings Claude:Effort.</summary>
    public string? Effort { get; set; }
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>Appended verbatim to the CLI args. E.g. "--allowedTools WebSearch" lets
    /// LlmResearch sources actually search the web. Configured via appsettings Claude:ExtraArgs.</summary>
    public string? ExtraArgs { get; set; }
}

public class ClaudeCliBackend(IProcessRunner runner, ClaudeCliOptions options) : ILlmBackend
{
    public string Name => "claude-cli";

    public async Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings,
        CancellationToken ct = default)
    {
        var args = "-p --output-format json";
        if (!string.IsNullOrWhiteSpace(settings.Model)) args += $" --model {settings.Model}";
        if (EffortFlag(settings.Effort) is { } effort) args += $" --effort {effort}";
        if (!string.IsNullOrWhiteSpace(options.ExtraArgs)) args += $" {options.ExtraArgs}";
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        string lastError = "";
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var result = await runner.RunAsync(options.Command, args, prompt, timeout, ct);
            if (result.ExitCode == 0 && TryParse(result.StdOut, out var text, out var model))
                return new LlmResult(text, model ?? NonBlank(settings.Model) ?? "claude-default");
            lastError = $"exit={result.ExitCode} stderr={result.StdErr} stdout={Truncate(result.StdOut)}";
        }
        throw new InvalidOperationException($"claude CLI failed after 2 attempts: {lastError}");
    }

    /// <summary>Claude CLI's --effort vocabulary, verified against v2.1.207.
    /// Intentionally a separate switch from LlmSettings.ToStorage: that one is the
    /// persistence format, this one is one provider's argument vocabulary. They
    /// read identically today by coincidence — do not collapse them, or a future
    /// backend with a different vocabulary forces a database migration.</summary>
    private static string? EffortFlag(LlmEffort effort) => effort switch
    {
        LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        LlmEffort.XHigh => "xhigh",
        LlmEffort.Max => "max",
        _ => null,
    };

    private static string? NonBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

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
