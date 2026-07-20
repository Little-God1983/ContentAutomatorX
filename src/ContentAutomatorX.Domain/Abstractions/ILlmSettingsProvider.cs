using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmSettingsProvider
{
    /// <summary>Never throws for a missing row — returns the fallback chain's result.</summary>
    Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Upserts this tenant's row.</summary>
    /// <exception cref="ArgumentException">The model name fails validation.</exception>
    Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default);
}
