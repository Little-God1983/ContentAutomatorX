using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class ContentService(IAppDbContext db)
{
    public async Task<List<ContentItem>> ListAsync(Guid tenantId, ContentItemStatus? status = null, DateTimeOffset? since = null)
    {
        var query = db.ContentItems.Where(i => i.TenantId == tenantId);
        if (status is not null) query = query.Where(i => i.Status == status);
        if (since is not null) query = query.Where(i => i.FetchedAt >= since);
        // SQLite cannot ORDER BY DateTimeOffset server-side, so materialize the filtered
        // query first and sort client-side (acceptable at this app's per-tenant scale).
        var list = await query.ToListAsync();
        return list.OrderByDescending(i => i.PublishedAt ?? i.FetchedAt).ToList();
    }

    /// <summary>
    /// Deletes the given items, except those already used in an issue — Used items carry
    /// provenance ("exclude already-used" selection) and would be re-ingested if removed.
    /// Returns how many were deleted and how many were kept because they are in use.
    /// </summary>
    public async Task<(int Deleted, int KeptUsed)> DeleteAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0) return (0, 0);
        var items = await db.ContentItems.Where(i => itemIds.Contains(i.Id)).ToListAsync();
        var deletable = items.Where(i => i.Status != ContentItemStatus.Used).ToList();
        db.ContentItems.RemoveRange(deletable);
        await db.SaveChangesAsync();
        return (deletable.Count, items.Count - deletable.Count);
    }

    /// <summary>Sets the item's curation status. Returns false when the item no longer exists.</summary>
    public async Task<bool> MarkAsync(Guid itemId, ContentItemStatus status)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null) return false;
        item.Status = status;
        await db.SaveChangesAsync();
        return true;
    }
}
