using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Pipelines;

public class IngestionPipeline(IAppDbContext db, IEnumerable<ISourceConnector> connectors)
{
    public async Task<PipelineRun> RunAsync(Guid tenantId, Guid? sourceId = null,
        string trigger = RunTriggers.Manual, CancellationToken ct = default)
    {
        var gate = TenantLocks.Get(tenantId);
        await gate.WaitAsync(ct);
        try { return await RunCoreAsync(tenantId, sourceId, trigger, ct); }
        finally { gate.Release(); }
    }

    private async Task<PipelineRun> RunCoreAsync(Guid tenantId, Guid? sourceId, string trigger, CancellationToken ct)
    {
        var run = new PipelineRun { TenantId = tenantId, Kind = RunKinds.Ingestion, Trigger = trigger };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var log = new List<string>();
        var sources = await db.Sources
            .Where(s => s.TenantId == tenantId && s.IsEnabled && (sourceId == null || s.Id == sourceId))
            .ToListAsync(ct);

        int failed = 0;
        foreach (var source in sources)
        {
            List<ContentItem> added = [];
            try
            {
                var connector = connectors.FirstOrDefault(c => c.Type == source.Type)
                    ?? throw new InvalidOperationException($"No connector for type '{source.Type}'");
                var fetched = await connector.FetchAsync(source, ct);
                var items = fetched.DistinctBy(f => f.ExternalId).ToList();

                var externalIds = items.Select(f => f.ExternalId).ToList();
                var existing = await db.ContentItems
                    .Where(i => i.SourceId == source.Id && externalIds.Contains(i.ExternalId))
                    .ToListAsync(ct);

                // refresh volatile metadata (score, listing rank) on re-fetched duplicates
                // so Inbox ordering reflects the latest fetch, not the first one
                foreach (var e in existing)
                    if (items.FirstOrDefault(f => f.ExternalId == e.ExternalId) is { } refetched)
                        e.MetadataJson = refetched.MetadataJson;

                var existingIds = existing.Select(i => i.ExternalId).ToHashSet();
                var fresh = items.Where(f => !existingIds.Contains(f.ExternalId)).ToList();

                // tenant-wide duplicate-link check on normalized URLs (cross-source; null = exempt)
                var normalized = fresh.Select(f => (Item: f, Norm: UrlNormalizer.Normalize(f.Url))).ToList();
                var norms = normalized.Where(x => x.Norm != null).Select(x => x.Norm!).Distinct().ToList();
                var owners = await db.ContentItems
                    .Where(i => i.TenantId == tenantId && i.NormalizedUrl != null && norms.Contains(i.NormalizedUrl))
                    .Select(i => new { i.NormalizedUrl, i.FetchedAt, i.SourceId })
                    .ToListAsync(ct);
                var ownerSourceIds = owners.Select(o => o.SourceId).Distinct().ToList();
                var ownerNames = await db.Sources
                    .Where(s => ownerSourceIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
                // NormalizedUrl is unique per tenant via the filtered unique index, so first-match
                // semantics are safe; ToDictionary is used directly rather than GroupBy+First.
                var ownerByNorm = owners.ToDictionary(o => o.NormalizedUrl!);

                var skipped = new List<string>();
                var seen = new HashSet<string>();
                var toAdd = new List<(Domain.Models.FetchedItem Item, string? Norm)>();
                foreach (var (f, norm) in normalized)
                {
                    if (norm == null) { toAdd.Add((f, null)); continue; }
                    if (ownerByNorm.TryGetValue(norm, out var owner))
                    {
                        var via = ownerNames.GetValueOrDefault(owner.SourceId, "unknown source");
                        skipped.Add($"  duplicate: {f.Url} (already imported {owner.FetchedAt:yyyy-MM-dd} via {via})");
                        continue;
                    }
                    if (!seen.Add(norm))
                    {
                        skipped.Add($"  duplicate: {f.Url} (duplicate within this fetch)");
                        continue;
                    }
                    toAdd.Add((f, norm));
                }

                added = toAdd.Select(x => new ContentItem
                {
                    TenantId = tenantId, SourceId = source.Id, ExternalId = x.Item.ExternalId,
                    Title = x.Item.Title, Url = x.Item.Url, Author = x.Item.Author, Body = x.Item.Body,
                    MetadataJson = x.Item.MetadataJson, PublishedAt = x.Item.PublishedAt,
                    NormalizedUrl = x.Norm
                }).ToList();
                foreach (var item in added) db.ContentItems.Add(item);

                source.LastFetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                log.Add($"{source.DisplayName}: fetched {fetched.Count}, new {added.Count}, skipped {skipped.Count} duplicate link(s)");
                log.AddRange(skipped);
            }
            catch (Exception ex)
            {
                failed++;
                log.Add($"{source.DisplayName}: FAILED - {ex.Message}");
                db.ContentItems.RemoveRange(added);
                source.LastFetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Status = failed == 0 ? RunStatus.Succeeded
                   : failed == sources.Count && sources.Count > 0 ? RunStatus.Failed
                   : RunStatus.Partial;
        run.LogJson = JsonSerializer.Serialize(log);
        await db.SaveChangesAsync(ct);
        return run;
    }
}
