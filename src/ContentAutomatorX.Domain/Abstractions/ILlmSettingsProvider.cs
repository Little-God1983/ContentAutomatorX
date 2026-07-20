using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmSettingsProvider
{
    /// <summary>Never throws for a missing row — returns the fallback chain's result.
    /// This is what generation runs on.</summary>
    Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>What this tenant itself stored, with no fallback applied —
    /// LlmSettings.Inherit when it has stored nothing. The editing UI needs this to
    /// tell "chose opus" apart from "inherited opus from appsettings"; showing the
    /// resolved value in a form makes Save silently convert an inherited value into
    /// an explicit one, and makes "Default" impossible to express.</summary>
    Task<LlmSettings> GetStoredAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Upserts this tenant's row.</summary>
    /// <exception cref="ArgumentException">The model name fails validation.</exception>
    Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default);
}
