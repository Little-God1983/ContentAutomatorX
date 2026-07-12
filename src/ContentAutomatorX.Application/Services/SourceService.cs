using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class SourceService(IAppDbContext db)
{
    public Task<List<Source>> ListAsync(Guid tenantId) =>
        db.Sources.Where(s => s.TenantId == tenantId).OrderBy(s => s.DisplayName).ToListAsync();

    public async Task<Source> CreateAsync(Source source)
    {
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    public Task UpdateAsync() => db.SaveChangesAsync();

    public async Task DeleteAsync(Guid id)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return;
        db.Sources.Remove(source);
        await db.SaveChangesAsync();
    }
}
