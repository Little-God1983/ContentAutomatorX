using ContentAutomatorX.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

/// <summary>
/// One-time (idempotent) startup pass that normalizes URLs of rows created before the
/// NormalizedUrl column existed. Collisions within a tenant keep the oldest row's value;
/// losers stay null so the filtered unique index can be satisfied without deleting data.
/// </summary>
public static class NormalizedUrlBackfill
{
    public static async Task RunAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var pending = (await db.ContentItems
            .Where(i => i.NormalizedUrl == null && i.Url != null)
            .ToListAsync(ct))
            .OrderBy(i => i.FetchedAt)
            .ToList();
        if (pending.Count == 0) return;

        var taken = (await db.ContentItems
                .Where(i => i.NormalizedUrl != null)
                .Select(i => new { i.TenantId, i.NormalizedUrl })
                .ToListAsync(ct))
            .Select(x => (x.TenantId, Norm: x.NormalizedUrl!))
            .ToHashSet();

        foreach (var item in pending)
        {
            var norm = UrlNormalizer.Normalize(item.Url);
            if (norm != null && taken.Add((item.TenantId, norm)))
                item.NormalizedUrl = norm;
        }
        await db.SaveChangesAsync(ct);
    }
}
