using System.ComponentModel;
using System.Text.Json;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ModelContextProtocol.Server;

namespace ContentAutomatorX.Web.Mcp;

[McpServerToolType]
public static class ContentXTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string ToJson(object value) => JsonSerializer.Serialize(value, Json);

    [McpServerTool(Name = "list_tenants"), Description("List all tenants (channels/brands) with ids, slugs and voice profiles.")]
    public static async Task<string> ListTenants(TenantService tenants) => ToJson(await tenants.ListAsync());

    [McpServerTool(Name = "get_tenant"), Description("Get one tenant by id.")]
    public static async Task<string> GetTenant(TenantService tenants, [Description("Tenant id (GUID)")] string tenantId) =>
        ToJson(await tenants.GetAsync(Guid.Parse(tenantId)) as object ?? "not found");

    [McpServerTool(Name = "list_sources"), Description("List a tenant's content sources (Reddit subreddits, RSS feeds).")]
    public static async Task<string> ListSources(SourceService sources, [Description("Tenant id (GUID)")] string tenantId) =>
        ToJson(await sources.ListAsync(Guid.Parse(tenantId)));

    [McpServerTool(Name = "trigger_ingestion"), Description("Fetch new items now for a tenant (optionally one source). Returns the pipeline run.")]
    public static async Task<string> TriggerIngestion(IngestionPipeline pipeline,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Optional source id (GUID) to fetch only that source")] string? sourceId = null)
    {
        var run = await pipeline.RunAsync(Guid.Parse(tenantId),
            sourceId is null ? null : Guid.Parse(sourceId), RunTriggers.Mcp);
        return ToJson(new { runStatus = run.Status.ToString(), log = run.LogJson });
    }

    [McpServerTool(Name = "list_content_items"), Description("Browse gathered content items for a tenant. Status: New|Selected|Ignored|Used.")]
    public static async Task<string> ListContentItems(ContentService content,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Optional status filter: New|Selected|Ignored|Used")] string? status = null,
        [Description("Optional: only items fetched in the last N days")] int? sinceDays = null)
    {
        ContentItemStatus? parsed = status is null ? null : Enum.Parse<ContentItemStatus>(status, ignoreCase: true);
        var since = sinceDays is int d ? DateTimeOffset.UtcNow.AddDays(-d) : (DateTimeOffset?)null;
        return ToJson(await content.ListAsync(Guid.Parse(tenantId), parsed, since));
    }

    [McpServerTool(Name = "mark_item"), Description("Curate a content item: set status Selected or Ignored (or back to New).")]
    public static async Task<string> MarkItem(ContentService content,
        [Description("Content item id (GUID)")] string itemId,
        [Description("New status: New|Selected|Ignored")] string status)
    {
        var parsed = Enum.Parse<ContentItemStatus>(status, ignoreCase: true);
        await content.MarkAsync(Guid.Parse(itemId), parsed);
        return ToJson(new { itemId, status = parsed.ToString() });
    }

    [McpServerTool(Name = "list_recipes"), Description("List a tenant's recipes (drafting configurations).")]
    public static async Task<string> ListRecipes(RecipeService recipes, [Description("Tenant id (GUID)")] string tenantId) =>
        ToJson(await recipes.ListAsync(Guid.Parse(tenantId)));

    [McpServerTool(Name = "get_recipe"), Description("Get one recipe by id, including selection rules and output config.")]
    public static async Task<string> GetRecipe(RecipeService recipes, [Description("Recipe id (GUID)")] string recipeId) =>
        ToJson(await recipes.GetAsync(Guid.Parse(recipeId)) as object ?? "not found");

    [McpServerTool(Name = "run_recipe"), Description("Run a recipe's generation pipeline now: select items, generate the draft via LLM, deliver the file. Returns run status, draft id and file path.")]
    public static async Task<string> RunRecipe(GenerationPipeline pipeline,
        [Description("Recipe id (GUID)")] string recipeId,
        [Description("Optional comma-separated content item ids (GUIDs) to use instead of the recipe's selection rules")] string? itemIds = null,
        [Description("Optional extra instructions for this run")] string? extraInstructions = null)
    {
        var ids = itemIds?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse).ToList();
        var (run, draft) = await pipeline.RunAsync(Guid.Parse(recipeId), ids, extraInstructions, RunTriggers.Mcp);
        return ToJson(new
        {
            runStatus = run.Status.ToString(),
            draftId = draft?.Id,
            title = draft?.Title,
            filePath = draft?.FilePath,
            log = run.LogJson
        });
    }

    [McpServerTool(Name = "list_drafts"), Description("List generated drafts for a tenant. Kind: Newsletter|SocialPost|VideoScript. Status: Generated|Delivered.")]
    public static async Task<string> ListDrafts(DraftService drafts,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Optional kind filter")] string? kind = null,
        [Description("Optional status filter: Generated|Delivered")] string? status = null)
    {
        DraftStatus? parsed = status is null ? null : Enum.Parse<DraftStatus>(status, ignoreCase: true);
        var list = await drafts.ListAsync(Guid.Parse(tenantId), kind, parsed);
        return ToJson(list.Select(d => new { d.Id, d.Kind, d.Title, d.Status, d.FilePath, d.CreatedAt }));
    }

    [McpServerTool(Name = "get_draft"), Description("Get one draft by id including its full Markdown body.")]
    public static async Task<string> GetDraft(DraftService drafts, [Description("Draft id (GUID)")] string draftId) =>
        ToJson(await drafts.GetAsync(Guid.Parse(draftId)) as object ?? "not found");

    [McpServerTool(Name = "get_pipeline_runs"), Description("Recent pipeline runs (ingestion/generation) for a tenant, newest first.")]
    public static async Task<string> GetPipelineRuns(RunService runs,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Max entries (default 20)")] int limit = 20) =>
        ToJson(await runs.ListAsync(Guid.Parse(tenantId), limit));
}
