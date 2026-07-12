using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class RunService(IAppDbContext db)
{
    public async Task<List<PipelineRun>> ListAsync(Guid tenantId, int limit = 50)
    {
        // SQLite cannot ORDER BY DateTimeOffset server-side, so materialize the filtered
        // query first and sort/limit client-side (acceptable at this app's per-tenant scale).
        var list = await db.PipelineRuns.Where(r => r.TenantId == tenantId).ToListAsync();
        return list.OrderByDescending(r => r.StartedAt).Take(limit).ToList();
    }
}
