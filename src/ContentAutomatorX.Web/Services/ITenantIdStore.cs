namespace ContentAutomatorX.Web.Services;

/// <summary>Persistence seam for the last-used tenant id (browser storage in production).</summary>
public interface ITenantIdStore
{
    Task<Guid?> GetAsync();
    Task SetAsync(Guid id);
}
