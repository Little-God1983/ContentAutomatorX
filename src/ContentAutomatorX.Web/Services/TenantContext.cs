using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Web.Services;

/// <summary>
/// Scoped (per-circuit) single source of truth for the active tenant.
/// Restores the last-used tenant from the store; falls back to the first
/// active tenant (TenantService.ListAsync order = by name), else null.
///
/// Restoring only pre-selects <see cref="Active"/> for highlighting on the
/// tenant picker — it does not, on its own, let the user into the app. The
/// picker calls <see cref="EnterAsync"/> to make selection explicit, which is
/// what flips <see cref="SelectionConfirmed"/>. Because this is scoped, that
/// flag resets on every new circuit, so the picker is shown on every app open.
/// </summary>
public class TenantContext(TenantService tenantSvc, ITenantIdStore store)
{
    public Tenant? Active { get; private set; }
    public IReadOnlyList<Tenant> ActiveTenants { get; private set; } = [];
    public bool Initialized { get; private set; }

    /// <summary>
    /// True once the user has explicitly picked a tenant this circuit (via the
    /// picker). The app shell stays gated behind the picker until this is set.
    /// </summary>
    public bool SelectionConfirmed { get; private set; }

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

    /// <summary>
    /// Explicitly enter the app as the given tenant from the picker. Unlike
    /// <see cref="SwitchAsync"/>, this always confirms and raises Changed even
    /// when the tenant is already <see cref="Active"/> (e.g. the remembered one
    /// the picker pre-highlighted) — clicking it must still let the user in.
    /// Unknown ids are ignored and leave the picker un-confirmed.
    /// </summary>
    public async Task EnterAsync(Guid tenantId)
    {
        var tenant = ActiveTenants.FirstOrDefault(t => t.Id == tenantId);
        if (tenant is null) return;
        Active = tenant;
        SelectionConfirmed = true;
        await PersistAsync(tenantId);
        Changed?.Invoke();
    }

    public async Task RefreshAsync(Guid? preferId = null)
    {
        await ResolveAsync(preferId ?? Active?.Id);
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
