using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class RunService(IAppDbContext db)
{
    public Task<List<PipelineRun>> ListAsync(Guid tenantId, int limit = 50) =>
        db.PipelineRuns.Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.StartedAt).Take(limit).ToListAsync();
}
