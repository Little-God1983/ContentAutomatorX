using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class TenantService(IAppDbContext db)
{
    public Task<List<Tenant>> ListAsync() => db.Tenants.OrderBy(t => t.Name).ToListAsync();
    public Task<Tenant?> GetAsync(Guid id) => db.Tenants.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Tenant> CreateAsync(Tenant tenant)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public Task UpdateAsync() => db.SaveChangesAsync();

    public async Task DeleteAsync(Guid id)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return;
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync();
    }
}
