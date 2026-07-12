using System.Text.Json;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Delivery;
using ContentAutomatorX.Web.Mcp;

namespace ContentAutomatorX.IntegrationTests;

public class McpToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"contentx-mcp-{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task List_tenants_returns_json_array()
    {
        using var test = TestDb.Create();
        test.Db.Tenants.Add(new Tenant { Name = "Chan", Slug = "chan" });
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.ListTenants(new TenantService(test.Db));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("chan", doc.RootElement[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Run_recipe_generates_and_reports_file_path()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t", OutputFolderPath = _dir };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "f" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.SocialPost, Template = "{items}{voice_profile}{tone_modifiers}{extra_instructions}" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.SocialPost, PromptTemplateId = template.Id };
        test.Db.Tenants.Add(tenant); test.Db.Sources.Add(source);
        test.Db.PromptTemplates.Add(template); test.Db.Recipes.Add(recipe);
        test.Db.ContentItems.Add(new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "e", Title = "News" });
        await test.Db.SaveChangesAsync();

        var pipeline = new GenerationPipeline(test.Db, new FakeLlm(), new FileShareDraftDelivery());
        var json = await ContentXTools.RunRecipe(pipeline, recipe.Id.ToString(), null, null);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Succeeded", doc.RootElement.GetProperty("runStatus").GetString());
        Assert.True(File.Exists(doc.RootElement.GetProperty("filePath").GetString()));
    }

    [Fact]
    public async Task Mark_item_changes_status()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "f" };
        var item = new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "e", Title = "i" };
        test.Db.Tenants.Add(tenant); test.Db.Sources.Add(source); test.Db.ContentItems.Add(item);
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.MarkItem(new ContentService(test.Db), item.Id.ToString(), "Selected");

        Assert.Contains("Selected", json);
    }

    [Fact]
    public async Task Get_tenant_returns_not_found_for_unknown_id_and_tenant_json_for_a_real_one()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "Chan", Slug = "chan2" };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();
        var service = new TenantService(test.Db);

        var notFoundJson = await ContentXTools.GetTenant(service, Guid.NewGuid().ToString());
        using var notFoundDoc = JsonDocument.Parse(notFoundJson);
        Assert.Equal(JsonValueKind.String, notFoundDoc.RootElement.ValueKind);
        Assert.Equal("not found", notFoundDoc.RootElement.GetString());

        var foundJson = await ContentXTools.GetTenant(service, tenant.Id.ToString());
        using var foundDoc = JsonDocument.Parse(foundJson);
        Assert.Equal("chan2", foundDoc.RootElement.GetProperty("slug").GetString());
    }

    // NOTE: Coverage follow-ups also called for tests of ListDrafts' projected shape and of
    // GetPipelineRuns' newest-first/limit ordering. Both are omitted here: DraftService.ListAsync
    // and RunService.ListAsync order directly on a DateTimeOffset column
    // (OrderByDescending(d => d.CreatedAt) / OrderByDescending(r => r.StartedAt)), which EF Core's
    // SQLite provider - the provider this app actually uses in production (see Program.cs
    // UseSqlite) - does not support ("SQLite does not support expressions of type 'DateTimeOffset'
    // in ORDER BY clauses"). The call fails at query-translation time regardless of row count, so
    // no test of this MCP tool's real behavior can pass without a production-code fix, which is out
    // of scope for this test-only pass. Reported as a concern; see the follow-ups report.
}
