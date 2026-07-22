using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmSettingsProvider
{
    /// <summary>Resolves the settings a given job runs on. Never throws for a missing row —
    /// returns the fallback chain's result: the job's own override, else the tenant default,
    /// else appsettings, else "omit the flag", resolved field-by-field. This is what generation
    /// runs on. <paramref name="job"/> is an <c>LlmJobs</c> key, or null for the tenant default
    /// (the behaviour before per-job overrides).</summary>
    Task<LlmSettings> GetAsync(Guid tenantId, string? job = null, CancellationToken ct = default);

    /// <summary>What this tenant itself stored for <paramref name="job"/> (null = the tenant
    /// default row), with no fallback applied — LlmSettings.Inherit when it has stored nothing.
    /// The editing UI needs this to tell "chose opus" apart from "inherited opus"; showing the
    /// resolved value in a form makes Save silently convert an inherited value into an explicit
    /// one, and makes "Default" impossible to express.</summary>
    Task<LlmSettings> GetStoredAsync(Guid tenantId, string? job = null, CancellationToken ct = default);

    /// <summary>Upserts this tenant's row for <paramref name="job"/> (null = the tenant default).</summary>
    /// <exception cref="ArgumentException">The model name fails validation.</exception>
    Task SaveAsync(Guid tenantId, LlmSettings settings, string? job = null, CancellationToken ct = default);
}
