using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class ContentService(IAppDbContext db)
{
    public Task<List<ContentItem>> ListAsync(Guid tenantId, ContentItemStatus? status = null, DateTimeOffset? since = null)
    {
        var query = db.ContentItems.Where(i => i.TenantId == tenantId);
        if (status is not null) query = query.Where(i => i.Status == status);
        if (since is not null) query = query.Where(i => i.FetchedAt >= since);
        return query.OrderByDescending(i => i.PublishedAt ?? i.FetchedAt).ToListAsync();
    }

    public async Task MarkAsync(Guid itemId, ContentItemStatus status)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Content item {itemId} not found");
        item.Status = status;
        await db.SaveChangesAsync();
    }
}
