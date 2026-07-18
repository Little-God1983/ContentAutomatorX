using System.Text.Json;
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Pipelines;

public class GenerationPipeline(IAppDbContext db, ILlmBackend llm, IDraftDelivery delivery)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<(PipelineRun Run, Draft? Draft)> RunAsync(Guid recipeId, IReadOnlyList<Guid>? itemIds = null,
        string? extraInstructions = null, string trigger = RunTriggers.Manual, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId, ct);
        var gate = TenantLocks.Get(recipe.TenantId);
        await gate.WaitAsync(ct);
        try { return await RunCoreAsync(recipe, itemIds, extraInstructions, trigger, ct); }
        finally { gate.Release(); }
    }

    private async Task<(PipelineRun, Draft?)> RunCoreAsync(Recipe recipe, IReadOnlyList<Guid>? itemIds,
        string? extraInstructions, string trigger, CancellationToken ct)
    {
        var run = new PipelineRun { TenantId = recipe.TenantId, Kind = RunKinds.Generation, Trigger = trigger };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);
        recipe.LastRunAt = DateTimeOffset.UtcNow;
        var log = new List<string> { $"recipe: {recipe.Name} ({recipe.Kind})" };

        try
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == recipe.TenantId, ct);
            var template = await db.PromptTemplates.FirstOrDefaultAsync(p => p.Id == recipe.PromptTemplateId, ct)
                ?? await db.PromptTemplates.FirstOrDefaultAsync(p => p.TenantId == null && p.Kind == recipe.Kind, ct);
            var templateText = template?.Template ?? DefaultTemplates.GetFor(recipe.Kind);

            var items = await SelectItemsAsync(recipe, itemIds, ct);
            if (items.Count == 0)
                return (await Finish(run, RunStatus.Failed, log, "no items matched the recipe selection", ct), null);
            log.Add($"selected {items.Count} items");

            var prompt = PromptBuilder.Build(templateText, tenant, recipe, items, extraInstructions);
            log.Add($"prompt: {prompt.Length} chars");

            LlmResult result;
            try { result = await llm.GenerateAsync(prompt, ct); }
            catch (Exception ex)
            {
                return (await Finish(run, RunStatus.Failed, log, $"LLM failed: {ex.Message}", ct), null);
            }

            var output = JsonSerializer.Deserialize<RecipeOutput>(recipe.OutputJson, JsonOpts) ?? new RecipeOutput();
            var draft = new Draft
            {
                TenantId = recipe.TenantId, RecipeId = recipe.Id, Kind = recipe.Kind,
                Title = ExtractTitle(result.Text) ?? $"{recipe.Name} — {DateTimeOffset.UtcNow:yyyy-MM-dd}",
                Body = result.Text, ModelUsed = result.Model,
                TargetPlatform = output.TargetPlatform,
                SourceItemIdsJson = JsonSerializer.Serialize(items.Select(i => i.Id.ToString()))
            };
            db.Drafts.Add(draft);
            foreach (var item in items) item.Status = ContentItemStatus.Used;
            await db.SaveChangesAsync(ct);

            if (recipe.TargetPlatformId is Guid platformId)
            {
                db.Posts.Add(new Post
                {
                    TenantId = recipe.TenantId, PlatformId = platformId, RecipeId = recipe.Id,
                    DraftId = draft.Id, Kind = recipe.Kind, Title = draft.Title,
                    Subject = draft.Title, Status = PostStatus.Draft, NeedsReview = true
                });
                await db.SaveChangesAsync(ct);
                log.Add("post created (review queue)");
            }

            try
            {
                draft.FilePath = await delivery.DeliverAsync(tenant, output, draft, ct);
                draft.Status = DraftStatus.Delivered;
                log.Add($"delivered: {draft.FilePath}");
                return (await Finish(run, RunStatus.Succeeded, log, null, ct), draft);
            }
            catch (Exception ex)
            {
                return (await Finish(run, RunStatus.Partial, log, $"delivery failed: {ex.Message}", ct), draft);
            }
        }
        catch (Exception ex)
        {
            return (await Finish(run, RunStatus.Failed, log, $"unexpected: {ex.Message}", ct), null);
        }
    }

    private async Task<List<ContentItem>> SelectItemsAsync(Recipe recipe, IReadOnlyList<Guid>? itemIds, CancellationToken ct)
    {
        if (itemIds is { Count: > 0 })
            return await db.ContentItems
                .Where(i => i.TenantId == recipe.TenantId && itemIds.Contains(i.Id))
                .ToListAsync(ct);

        var sourceIds = JsonSerializer.Deserialize<Guid[]>(recipe.SourceIdsJson) ?? [];
        var candidatesQuery = db.ContentItems.Where(i => i.TenantId == recipe.TenantId);
        if (sourceIds.Length > 0)
            candidatesQuery = candidatesQuery.Where(i => sourceIds.Contains(i.SourceId));
        var candidates = await candidatesQuery.ToListAsync(ct);

        var priorDrafts = await db.Drafts
            .Where(d => d.RecipeId == recipe.Id)
            .Select(d => d.SourceItemIdsJson)
            .ToListAsync(ct);
        var used = priorDrafts
            .SelectMany(j => JsonSerializer.Deserialize<string[]>(j) ?? [])
            .Select(Guid.Parse)
            .ToHashSet();

        var rules = JsonSerializer.Deserialize<SelectionRules>(recipe.SelectionJson, JsonOpts) ?? new SelectionRules();
        return ItemSelector.Select(candidates, rules, used, DateTimeOffset.UtcNow);
    }

    private static string? ExtractTitle(string markdown)
    {
        var firstLine = markdown.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith('#'));
        return firstLine?.TrimStart('#', ' ').Trim() is { Length: > 0 } t ? t : null;
    }

    private async Task<PipelineRun> Finish(PipelineRun run, RunStatus status, List<string> log, string? message, CancellationToken ct)
    {
        if (message is not null) log.Add(message);
        run.Status = status;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.LogJson = JsonSerializer.Serialize(log);
        await db.SaveChangesAsync(ct);
        return run;
    }
}
