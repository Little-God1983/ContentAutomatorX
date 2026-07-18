using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Web.Services;

/// <summary>
/// Scoped (per-circuit) single source of truth for the active tenant.
/// Restores the last-used tenant from the store; falls back to the first
/// active tenant (TenantService.ListAsync order = by name), else null.
/// </summary>
public class TenantContext(TenantService tenantSvc, ITenantIdStore store)
{
    public Tenant? Active { get; private set; }
    public IReadOnlyList<Tenant> ActiveTenants { get; private set; } = [];
    public bool Initialized { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (Initialized) return;
        Guid? stored;
        try { stored = await store.GetAsync(); }
        catch { stored = null; }   // unreadable browser storage = no stored id
        await ResolveAsync(stored);
        Initialized = true;
        Changed?.Invoke();
    }

    public async Task SwitchAsync(Guid tenantId)
    {
        var tenant = ActiveTenants.FirstOrDefault(t => t.Id == tenantId);
        if (tenant is null || tenant.Id == Active?.Id) return;
        Active = tenant;
        await PersistAsync(tenantId);
        Changed?.Invoke();
    }

    public async Task RefreshAsync()
    {
        await ResolveAsync(Active?.Id);
        Changed?.Invoke();
    }

    private async Task ResolveAsync(Guid? preferredId)
    {
        ActiveTenants = (await tenantSvc.ListAsync()).Where(t => t.IsActive).ToList();
        Active = ActiveTenants.FirstOrDefault(t => t.Id == preferredId) ?? ActiveTenants.FirstOrDefault();
        if (Active is not null) await PersistAsync(Active.Id);
    }

    private async Task PersistAsync(Guid id)
    {
        try { await store.SetAsync(id); } catch { /* best effort */ }
    }
}
