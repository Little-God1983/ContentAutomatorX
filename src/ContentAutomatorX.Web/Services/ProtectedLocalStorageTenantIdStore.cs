using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace ContentAutomatorX.Web.Services;

/// <summary>Persists the active tenant id in encrypted browser localStorage (survives refresh/new tabs).</summary>
public class ProtectedLocalStorageTenantIdStore(ProtectedLocalStorage storage) : ITenantIdStore
{
    private const string Key = "contentx-active-tenant";

    public async Task<Guid?> GetAsync()
    {
        // Data-protection key rotation or a tampered payload throws — treat as "no stored id".
        try
        {
            var result = await storage.GetAsync<Guid>(Key);
            return result.Success ? result.Value : null;
        }
        catch { return null; }
    }

    public async Task SetAsync(Guid id)
    {
        try { await storage.SetAsync(Key, id); } catch { /* best effort */ }
    }
}
