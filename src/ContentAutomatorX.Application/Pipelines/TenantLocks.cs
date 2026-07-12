using System.Collections.Concurrent;

namespace ContentAutomatorX.Application.Pipelines;

/// <summary>One pipeline run per tenant at a time (spec: concurrency rules).</summary>
public static class TenantLocks
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();
    public static SemaphoreSlim Get(Guid tenantId) => Locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
}
