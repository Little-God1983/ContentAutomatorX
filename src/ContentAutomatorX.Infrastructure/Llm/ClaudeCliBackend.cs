using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Llm;

public class ClaudeCliOptions
{
    /// <summary>Executable name/path. On Windows, if plain "claude" fails to start,
    /// set the full path (e.g. %LOCALAPPDATA%\...\claude.exe) in appsettings Claude:Command.</summary>
    public string Command { get; set; } = "claude";

    /// <summary>Fallback model for tenants that have not chosen one. Not read by
    /// GenerateAsync — that takes the model from the LlmSettings passed per call.
    /// Configured via appsettings Claude:Model.</summary>
    public string? Model { get; set; }

    /// <summary>Fallback reasoning depth for tenants that have not chosen one.
    /// Storage vocabulary: "", low, medium, high, xhigh, max. Configured via
    /// appsettings Claude:Effort.</summary>
    public string? Effort { get; set; }
    /// <summary>Wall-clock cap for the run-to-completion <c>GenerateAsync</c> path.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>Idle cap for the streaming path — the maximum gap between output lines before a
    /// stalled generation is treated as a hang. Distinct from <see cref="TimeoutSeconds"/>: a long
    /// but steadily-streaming reply is healthy, so streaming cannot use a wall-clock limit without
    /// killing legitimately slow generations. Configured via appsettings Claude:IdleTimeoutSeconds.</summary>
    public int IdleTimeoutSeconds { get; set; } = 120;

    /// <summary>Appended verbatim to the CLI args. E.g. "--allowedTools WebSearch" lets
    /// LlmResearch sources actually search the web. Configured via appsettings Claude:ExtraArgs.</summary>
    public string? ExtraArgs { get; set; }
}

public class ClaudeCliBackend(IProcessRunner runner, ClaudeCliOptions options) : IStreamingLlmBackend
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

    public async IAsyncEnumerable<LlmChunk> StreamAsync(string prompt, LlmSettings settings,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = StreamArgs(settings);
        var idle = TimeSpan.FromSeconds(options.IdleTimeoutSeconds);

        // Track whether token-level deltas showed up. If they did, ignore the whole-message
        // "assistant" event (it repeats the same text and would double the progress display); if the
        // CLI build does not emit partial messages, fall back to that whole-message text for progress.
        var sawDelta = false;
        await foreach (var line in runner.RunStreamingAsync(options.Command, args, prompt, idle, ct))
        {
            if (TryParseFinal(line, out var finalText, out var model))
                yield return new LlmChunk(finalText, IsFinal: true,
                    Model: model ?? NonBlank(settings.Model) ?? "claude-default");
            else if (TryParseDeltaText(line, out var delta))
            {
                sawDelta = true;
                yield return new LlmChunk(delta);
            }
            else if (!sawDelta && TryParseAssistantText(line, out var whole))
                yield return new LlmChunk(whole);
        }
    }

    private string StreamArgs(LlmSettings settings)
    {
        // stream-json in print mode requires --verbose; --include-partial-messages turns on the
        // token-level content_block_delta events (without it the reply arrives as one assistant
        // event). The final "result" event is identical in shape to --output-format json, so its
        // text is parsed by the same TryParse the batch path uses — guaranteeing the streamed final
        // text equals GenerateAsync's for the same reply.
        var args = "-p --output-format stream-json --verbose --include-partial-messages";
        if (!string.IsNullOrWhiteSpace(settings.Model)) args += $" --model {settings.Model}";
        if (EffortFlag(settings.Effort) is { } effort) args += $" --effort {effort}";
        if (!string.IsNullOrWhiteSpace(options.ExtraArgs)) args += $" {options.ExtraArgs}";
        return args;
    }

    /// <summary>The terminal "result" NDJSON event — same shape as --output-format json output.</summary>
    private static bool TryParseFinal(string line, out string text, out string? model)
    {
        text = "";
        model = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "result") return false;
            if (root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True) return false;
            if (!root.TryGetProperty("result", out var r) || r.ValueKind != JsonValueKind.String) return false;
            text = r.GetString() ?? "";
            model = ExtractModel(root);
            return text.Length > 0;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>A token-level content_block_delta from --include-partial-messages.</summary>
    private static bool TryParseDeltaText(string line, out string text)
    {
        text = "";
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "stream_event") return false;
            if (!root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object) return false;
            if (!ev.TryGetProperty("type", out var et) || et.GetString() != "content_block_delta") return false;
            if (!ev.TryGetProperty("delta", out var d) || d.ValueKind != JsonValueKind.Object) return false;
            if (!d.TryGetProperty("text", out var dt) || dt.ValueKind != JsonValueKind.String) return false;
            text = dt.GetString() ?? "";
            return text.Length > 0;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>The whole-message "assistant" event — the fallback progress source when the CLI build
    /// does not emit partial-message deltas.</summary>
    private static bool TryParseAssistantText(string line, out string text)
    {
        text = "";
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "assistant") return false;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return false;
            if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return false;
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
                if (block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                    && block.TryGetProperty("text", out var bx) && bx.ValueKind == JsonValueKind.String)
                    sb.Append(bx.GetString());
            text = sb.ToString();
            return text.Length > 0;
        }
        catch (JsonException) { return false; }
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
            model = ExtractModel(root);
            return text.Length > 0;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Real CLI output has no top-level "model" field; the model actually used is the (sole)
    /// key of "modelUsage". A root "model" string is supported too in case a future version adds one.
    /// Shared by the batch result parse and the streaming terminal-event parse.</summary>
    private static string? ExtractModel(JsonElement root)
    {
        if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
            return m.GetString();
        if (root.TryGetProperty("modelUsage", out var mu) && mu.ValueKind == JsonValueKind.Object)
            foreach (var prop in mu.EnumerateObject())
                return prop.Name;
        return null;
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}
