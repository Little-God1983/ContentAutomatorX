namespace ContentAutomatorX.Domain.Models;

/// <summary>How hard the model should think. Provider-neutral: each backend
/// translates this to its own vocabulary (Claude CLI --effort, an
/// OpenAI-compatible reasoning_effort, or nothing at all).</summary>
public enum LlmEffort { Default, Low, Medium, High, XHigh, Max }

/// <summary>Resolved per-tenant LLM choice, passed into every ILlmBackend call.
/// Names nothing Claude-specific.</summary>
/// <param name="Model">Alias or full model ID. "" means "omit the flag".</param>
public record LlmSettings(string Model, LlmEffort Effort)
{
    /// <summary>Change nothing — let the backend's own default stand.</summary>
    public static readonly LlmSettings Inherit = new("", LlmEffort.Default);

    public static LlmSettings From(string? model, string? effort) =>
        new(model?.Trim() ?? "", ParseEffort(effort));

    /// <summary>Canonical persisted form. Deliberately a string, not the enum's
    /// int, so the column stays readable and survives the enum being reordered.</summary>
    public static string ToStorage(LlmEffort effort) => effort switch
    {
        LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        LlmEffort.XHigh => "xhigh",
        LlmEffort.Max => "max",
        _ => "",
    };

    /// <summary>Never throws. A row hand-edited to garbage, or written by a
    /// future version that knows more levels, degrades to Default (flag omitted)
    /// rather than bricking generation for that tenant.</summary>
    public static LlmEffort ParseEffort(string? stored) => stored?.Trim().ToLowerInvariant() switch
    {
        "low" => LlmEffort.Low,
        "medium" => LlmEffort.Medium,
        "high" => LlmEffort.High,
        "xhigh" => LlmEffort.XHigh,
        "max" => LlmEffort.Max,
        _ => LlmEffort.Default,
    };
}

/// <summary>The appsettings-derived fallback, for tenants that have chosen nothing.
///
/// A distinct type rather than a bare LlmSettings in the container, because those
/// two things are not interchangeable: this is one global default, not any tenant's
/// answer. Registered as LlmSettings it would satisfy any future component that
/// injects LlmSettings — and that component would run, silently, on the wrong
/// settings for every tenant. Same reasoning as ILlmBackend.GenerateAsync taking a
/// required settings parameter: make the wrong thing fail to compile.</summary>
public sealed record LlmFallbackSettings(LlmSettings Value);
