using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Domain.Abstractions;

/// <summary>Phase 2 seam (YouTube/Patreon/Civitai/Ko-fi). No implementations in Phase 1.</summary>
public interface IPlatformConnector
{
    string Platform { get; }
    Task PublishAsync(Tenant tenant, Draft draft, CancellationToken ct = default);
}
