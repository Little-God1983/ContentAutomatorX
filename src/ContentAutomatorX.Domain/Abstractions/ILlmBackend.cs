using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmBackend
{
    string Name { get; }

    /// <param name="settings">Which model and how hard it thinks, already resolved
    /// for the calling tenant. Required — deliberately NOT defaulted to null: a
    /// default would let a missed call site compile and silently run on another
    /// tenant's model, which is the exact bug per-tenant settings exist to prevent.</param>
    Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default);
}
