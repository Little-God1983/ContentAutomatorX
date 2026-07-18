using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
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
                added = fresh.Select(f => new ContentItem
                {
                    TenantId = tenantId, SourceId = source.Id, ExternalId = f.ExternalId,
                    Title = f.Title, Url = f.Url, Author = f.Author, Body = f.Body,
                    MetadataJson = f.MetadataJson, PublishedAt = f.PublishedAt
                }).ToList();
                foreach (var item in added) db.ContentItems.Add(item);

                source.LastFetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                log.Add($"{source.DisplayName}: fetched {fetched.Count}, new {fresh.Count}");
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
