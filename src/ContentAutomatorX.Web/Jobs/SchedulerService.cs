using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Scheduling;
using ContentAutomatorX.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Web.Jobs;

public class SchedulerService(IServiceScopeFactory scopeFactory, ILogger<SchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try { await TickAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "scheduler tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // due sources → ingestion
        List<(Guid TenantId, Guid SourceId)> dueSources;
        List<Guid> dueRecipes;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            dueSources = (await db.Sources.AsNoTracking()
                    .Where(s => s.IsEnabled && s.ScheduleCron != null)
                    .ToListAsync(ct))
                .Where(s => CronDue.IsDue(s.ScheduleCron!, s.LastFetchedAt, now))
                .Select(s => (s.TenantId, s.Id))
                .ToList();
            dueRecipes = (await db.Recipes.AsNoTracking()
                    .Where(r => r.IsEnabled && r.ScheduleCron != null)
                    .ToListAsync(ct))
                .Where(r => CronDue.IsDue(r.ScheduleCron!, r.LastRunAt, now))
                .Select(r => r.Id)
                .ToList();
        }

        foreach (var (tenantId, sourceId) in dueSources)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();
                await ingestion.RunAsync(tenantId, sourceId, RunTriggers.Scheduled, ct);
            }
            catch (Exception ex) { logger.LogError(ex, "scheduled ingestion failed for source {SourceId}", sourceId); }
        }

        foreach (var recipeId in dueRecipes)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var recipe = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipeId, ct);

                // full auto: ingest the recipe's sources first, then generate
                var sourceIds = JsonSerializer.Deserialize<Guid[]>(recipe.SourceIdsJson) ?? [];
                var ingestion = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();
                if (sourceIds.Length == 0)
                    await ingestion.RunAsync(recipe.TenantId, null, RunTriggers.Scheduled, ct);
                else
                    foreach (var sourceId in sourceIds)
                        await ingestion.RunAsync(recipe.TenantId, sourceId, RunTriggers.Scheduled, ct);

                var generation = scope.ServiceProvider.GetRequiredService<GenerationPipeline>();
                await generation.RunAsync(recipeId, trigger: RunTriggers.Scheduled, ct: ct);
            }
            catch (Exception ex) { logger.LogError(ex, "scheduled recipe run failed for {RecipeId}", recipeId); }
        }
    }
}
