using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
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

        var pipeline = new GenerationPipeline(test.Db, new FakeLlm(), new FileShareDraftDelivery(), new StubLlmSettings());
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
        using var fresh = test.NewContext();
        Assert.Equal(ContentItemStatus.Selected, fresh.ContentItems.Single(i => i.Id == item.Id).Status);
    }

    [Fact]
    public async Task Mark_item_unknown_id_returns_not_found()
    {
        using var test = TestDb.Create();

        var json = await ContentXTools.MarkItem(new ContentService(test.Db), Guid.NewGuid().ToString(), "Selected");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, doc.RootElement.ValueKind);
        Assert.Equal("not found", doc.RootElement.GetString());
    }

    [Fact]
    public async Task Push_post_unknown_id_returns_not_found()
    {
        using var test = TestDb.Create();
        var ml = new FakeMailerLite();
        var creds = new InMemoryCredentials();
        var platforms = new PlatformService(test.Db, creds, ml);
        var generation = new GenerationPipeline(test.Db, new FakeLlm(), new FakeDelivery(), new StubLlmSettings());
        var posts = new PostService(test.Db, generation, new FakeLlm(), platforms, ml, new StubLlmSettings());

        var json = await ContentXTools.PushPost(posts, Guid.NewGuid().ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, doc.RootElement.ValueKind);
        Assert.Equal("not found", doc.RootElement.GetString());
    }

    [Fact]
    public async Task List_sources_returns_the_tenants_sources()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-sources" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        test.Db.Tenants.Add(tenant); test.Db.Sources.Add(source);
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.ListSources(new SourceService(test.Db), tenant.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        var element = doc.RootElement[0];
        Assert.Equal(source.Id, element.GetProperty("id").GetGuid());
        Assert.Equal(SourceTypes.Rss, element.GetProperty("type").GetString());
    }

    [Fact]
    public async Task List_content_items_filters_by_status()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-items" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        var newItem = new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "n", Title = "New Item", Status = ContentItemStatus.New };
        var selectedItem = new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "s", Title = "Selected Item", Status = ContentItemStatus.Selected };
        test.Db.Tenants.Add(tenant); test.Db.Sources.Add(source);
        test.Db.ContentItems.AddRange(newItem, selectedItem);
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.ListContentItems(new ContentService(test.Db), tenant.Id.ToString(), "Selected");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal(selectedItem.Id, doc.RootElement[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_recipes_returns_the_tenants_recipes()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-recipes" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "My Recipe", Kind = DraftKinds.SocialPost };
        test.Db.Tenants.Add(tenant); test.Db.Recipes.Add(recipe);
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.ListRecipes(new RecipeService(test.Db), tenant.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("My Recipe", doc.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_recipe_returns_not_found_for_unknown_and_json_for_real()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-getrecipe" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "R1", Kind = DraftKinds.SocialPost };
        test.Db.Tenants.Add(tenant); test.Db.Recipes.Add(recipe);
        await test.Db.SaveChangesAsync();
        var service = new RecipeService(test.Db);

        var notFoundJson = await ContentXTools.GetRecipe(service, Guid.NewGuid().ToString());
        using var notFoundDoc = JsonDocument.Parse(notFoundJson);
        Assert.Equal(JsonValueKind.String, notFoundDoc.RootElement.ValueKind);
        Assert.Equal("not found", notFoundDoc.RootElement.GetString());

        var foundJson = await ContentXTools.GetRecipe(service, recipe.Id.ToString());
        using var foundDoc = JsonDocument.Parse(foundJson);
        Assert.Equal("R1", foundDoc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_draft_returns_not_found_for_unknown_id()
    {
        using var test = TestDb.Create();

        var json = await ContentXTools.GetDraft(new DraftService(test.Db, new FileShareDraftDelivery()), Guid.NewGuid().ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, doc.RootElement.ValueKind);
        Assert.Equal("not found", doc.RootElement.GetString());
    }

    [Fact]
    public async Task Trigger_ingestion_reports_run_status()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-trigger" };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();

        var pipeline = new IngestionPipeline(test.Db, []);
        var json = await ContentXTools.TriggerIngestion(pipeline, tenant.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("runStatus", out var runStatus));
        Assert.Equal("Succeeded", runStatus.GetString());
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

    [Fact]
    public async Task List_drafts_returns_the_projected_shape()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-drafts" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.SocialPost };
        var draft = new Draft
        {
            TenantId = tenant.Id, RecipeId = recipe.Id, Kind = DraftKinds.SocialPost,
            Title = "My Post", Status = DraftStatus.Generated, FilePath = "/out/my-post.md"
        };
        test.Db.Tenants.Add(tenant); test.Db.Recipes.Add(recipe); test.Db.Drafts.Add(draft);
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.ListDrafts(new DraftService(test.Db, new FileShareDraftDelivery()), tenant.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        var element = doc.RootElement[0];
        Assert.Equal(draft.Id, element.GetProperty("id").GetGuid());
        Assert.Equal(DraftKinds.SocialPost, element.GetProperty("kind").GetString());
        Assert.Equal("My Post", element.GetProperty("title").GetString());
        Assert.Equal("Generated", element.GetProperty("status").GetString());
        Assert.Equal("/out/my-post.md", element.GetProperty("filePath").GetString());
    }

    [Fact]
    public async Task Get_pipeline_runs_returns_newest_first_with_limit_applied()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-runs" };
        var now = DateTimeOffset.UtcNow;
        var oldest = new PipelineRun { TenantId = tenant.Id, Kind = RunKinds.Ingestion, Trigger = RunTriggers.Mcp, StartedAt = now.AddMinutes(-10) };
        var middle = new PipelineRun { TenantId = tenant.Id, Kind = RunKinds.Ingestion, Trigger = RunTriggers.Mcp, StartedAt = now.AddMinutes(-5) };
        var newest = new PipelineRun { TenantId = tenant.Id, Kind = RunKinds.Ingestion, Trigger = RunTriggers.Mcp, StartedAt = now };
        test.Db.Tenants.Add(tenant);
        test.Db.PipelineRuns.AddRange(oldest, middle, newest);
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.GetPipelineRuns(new RunService(test.Db), tenant.Id.ToString(), 2);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal(newest.Id, doc.RootElement[0].GetProperty("id").GetGuid());
        Assert.Equal(middle.Id, doc.RootElement[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_posts_returns_the_projected_shape()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-posts" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post
        {
            TenantId = tenant.Id,
            PlatformId = platform.Id,
            Kind = DraftKinds.Newsletter,
            Title = "Issue #1",
            Status = PostStatus.Pushed,
            NeedsReview = false,
            ExternalUrl = "https://example.com/post/1"
        };
        test.Db.Tenants.Add(tenant);
        test.Db.Platforms.Add(platform);
        test.Db.Posts.Add(post);
        await test.Db.SaveChangesAsync();

        var ml = new FakeMailerLite();
        var creds = new InMemoryCredentials();
        var platforms = new PlatformService(test.Db, creds, ml);
        var generation = new GenerationPipeline(test.Db, new FakeLlm(), new FakeDelivery(), new StubLlmSettings());
        var posts = new PostService(test.Db, generation, new FakeLlm(), platforms, ml, new StubLlmSettings());

        var json = await ContentXTools.ListPosts(posts, tenant.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        var element = doc.RootElement[0];
        Assert.Equal(post.Id, element.GetProperty("id").GetGuid());
        Assert.Equal("Issue #1", element.GetProperty("title").GetString());
        Assert.Equal("Pushed", element.GetProperty("status").GetString());
        Assert.False(element.GetProperty("needsReview").GetBoolean());
        Assert.Equal("https://example.com/post/1", element.GetProperty("externalUrl").GetString());
    }
}
